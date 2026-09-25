// WeatherAlert Go — Termux 零依赖天气预警 CLI（中央气象台 nmc.cn 公开数据）
package main

import (
	"context"
	"crypto/tls"
	"crypto/x509"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/url"
	"os"
	"regexp"
	"sort"
	"strconv"
	"strings"
	"time"
)

const (
	listURL = "https://www.nmc.cn/rest/findAlarm"
	pageURL = "https://www.nmc.cn/publish/alarm"
)

var provinces = []string{
	"北京市", "天津市", "河北省", "山西省", "内蒙古自治区",
	"辽宁省", "吉林省", "黑龙江省", "上海市", "江苏省",
	"浙江省", "安徽省", "福建省", "江西省", "山东省",
	"河南省", "湖北省", "湖南省", "广东省", "广西壮族自治区",
	"海南省", "重庆市", "四川省", "贵州省", "云南省",
	"西藏自治区", "陕西省", "甘肃省", "青海省", "宁夏回族自治区",
	"新疆维吾尔自治区", "台湾省", "香港", "澳门",
}

var alertTypes = []string{
	"暴雨", "台风", "暴雪", "寒潮", "大风", "沙尘暴",
	"高温", "干旱", "雷电", "冰雹", "霜冻", "大雾", "道路结冰",
}

var alertLevels = []string{"蓝色", "黄色", "橙色", "红色"}

var levelColors = map[string]string{
	"红色": "\033[31m", "橙色": "\033[38;5;208m",
	"黄色": "\033[33m", "蓝色": "\033[36m",
}

const colorReset = "\033[0m"

type alert struct {
	ID, Title, SignalType, SignalLevel, Location, IssueTime string
}

func (a alert) normalizedLevel() string {
	for _, lv := range alertLevels {
		if strings.Contains(a.SignalLevel, lv) {
			return lv
		}
	}
	return ""
}

func (a alert) levelWeight() int {
	switch a.normalizedLevel() {
	case "红色":
		return 3
	case "橙色":
		return 2
	case "黄色":
		return 1
	default:
		return 0
	}
}

type apiResp struct {
	Data struct {
		Page struct {
			Count int `json:"count"`
			List  []struct {
				AlertID   string `json:"alertid"`
				IssueTime string `json:"issuetime"`
				Title     string `json:"title"`
			} `json:"list"`
		} `json:"page"`
		Stat map[string]map[string]int `json:"stat"`
	} `json:"data"`
}

// makeDNSResolver: Android/Termux 上 Go 纯解析器读不到系统 DNS（/etc/resolv.conf 缺失或指向 ::1），
// 这里手动指定 DNS：优先 Termux 的 $PREFIX/etc/resolv.conf，兜底公共 DNS
func makeDNSResolver() *net.Resolver {
	var servers []string
	for _, p := range []string{"/etc/resolv.conf", os.Getenv("PREFIX") + "/etc/resolv.conf"} {
		if data, err := os.ReadFile(p); err == nil {
			for _, line := range strings.Split(string(data), "\n") {
				if strings.HasPrefix(strings.TrimSpace(line), "nameserver ") {
					if ns := strings.TrimSpace(strings.TrimPrefix(strings.TrimSpace(line), "nameserver ")); ns != "" && ns != "::1" && ns != "127.0.0.1" {
						servers = append(servers, ns)
					}
				}
			}
			if len(servers) > 0 {
				break
			}
		}
	}
	if len(servers) == 0 {
		servers = []string{"223.5.5.5", "119.29.29.29", "8.8.8.8"}
	}
	return &net.Resolver{
		PreferGo: true,
		Dial: func(ctx context.Context, _, _ string) (net.Conn, error) {
			d := net.Dialer{Timeout: 5 * time.Second}
			var lastErr error
			for _, ns := range servers {
				if c, err := d.DialContext(ctx, "udp", net.JoinHostPort(ns, "53")); err == nil {
					return c, nil
				} else {
					lastErr = err
				}
			}
			return nil, lastErr
		},
	}
}

// loadRootCAs: Android/Termux 上 Go 找不到系统 CA 证书路径，手动加载：
// 优先 Termux 的 $PREFIX/etc/tls/cert.pem，其次常见 Linux 路径，最后 Android 系统证书目录
func loadRootCAs() *x509.CertPool {
	pool := x509.NewCertPool()
	for _, p := range []string{
		os.Getenv("PREFIX") + "/etc/tls/cert.pem",
		"/etc/ssl/certs/ca-certificates.crt",
		"/etc/pki/tls/certs/ca-bundle.crt",
		"/usr/local/share/certs/ca-root-nss.crt",
	} {
		if data, err := os.ReadFile(p); err == nil && pool.AppendCertsFromPEM(data) {
			return pool
		}
	}
	if entries, err := os.ReadDir("/system/etc/security/cacerts"); err == nil {
		for _, e := range entries {
			if data, err := os.ReadFile("/system/etc/security/cacerts/" + e.Name()); err == nil {
				pool.AppendCertsFromPEM(data)
			}
		}
		if len(pool.Subjects()) > 0 {
			return pool
		}
	}
	return nil // 找不到任何证书时用 Go 内置默认
}

var client = &http.Client{
	Timeout: 20 * time.Second,
	Transport: &http.Transport{
		Proxy: http.ProxyFromEnvironment,
		DialContext: (&net.Dialer{
			Timeout:  10 * time.Second,
			Resolver: makeDNSResolver(),
		}).DialContext,
		TLSClientConfig: &tls.Config{
			RootCAs:            loadRootCAs(),
			InsecureSkipVerify: os.Getenv("WA_INSECURE") == "1", // 应急逃生口，仅调试用
		},
	},
}

func httpGet(url string) ([]byte, error) {
	req, err := http.NewRequest("GET", url, nil)
	if err != nil {
		return nil, err
	}
	req.Header.Set("User-Agent", "Mozilla/5.0 (Linux; Android 13) Chrome/120 Mobile")
	req.Header.Set("Accept", "application/json")
	req.Header.Set("Referer", "https://www.nmc.cn/publish/alarm.html")
	resp, err := client.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	return io.ReadAll(resp.Body)
}

var titleRe = regexp.MustCompile(`^(.+?)发布(.+?)(蓝色|黄色|橙色|红色)预警信号?$`)

func fetchAlerts() ([]alert, *apiResp, error) {
	body, err := httpGet(listURL + "?pageNo=1&pageSize=500")
	if err != nil {
		return nil, nil, err
	}
	var r apiResp
	if err := json.Unmarshal(body, &r); err != nil {
		return nil, nil, fmt.Errorf("接口返回格式异常（可能被网关拦截）")
	}
	var alerts []alert
	for _, it := range r.Data.Page.List {
		if it.AlertID == "" || it.Title == "" {
			continue
		}
		a := alert{ID: it.AlertID, Title: it.Title, IssueTime: it.IssueTime}
		if m := titleRe.FindStringSubmatch(it.Title); m != nil {
			a.Location, a.SignalType, a.SignalLevel = m[1], m[2], m[3]
		}
		alerts = append(alerts, a)
	}
	sort.SliceStable(alerts, func(i, j int) bool {
		return alerts[i].levelWeight() > alerts[j].levelWeight()
	})
	return alerts, &r, nil
}

func detailURL(id string) string {
	return pageURL + "/" + url.PathEscape(id) + ".html"
}

var alarmTextRe = regexp.MustCompile(`(?is)<div\s+id="?alarmtext"?[^>]*>(.*?)</div>`)

func stripHTML(s string) string {
	s = regexp.MustCompile(`(?i)<br\s*/?>`).ReplaceAllString(s, "\n")
	s = regexp.MustCompile(`<[^>]+>`).ReplaceAllString(s, "")
	s = regexp.MustCompile(`\n{3,}`).ReplaceAllString(s, "\n\n")
	return strings.NewReplacer("&nbsp;", " ", "&lt;", "<", "&gt;", ">",
		"&amp;", "&", "&quot;", "\"").Replace(s)
}

func fetchDetail(id string) (string, error) {
	body, err := httpGet(detailURL(id))
	if err != nil {
		return "", err
	}
	matches := alarmTextRe.FindAllStringSubmatch(string(body), -1)
	if len(matches) == 0 {
		return "", fmt.Errorf("详情页解析失败")
	}
	var parts []string
	for _, m := range matches {
		if t := strings.TrimSpace(stripHTML(m[1])); t != "" {
			parts = append(parts, t)
		}
	}
	return strings.Join(parts, "\n\n"), nil
}

func printUsage() {
	fmt.Println(`天气预警查询（Go 版，零依赖）
用法:
  weather-alert list   [--province 省份] [--type 类型] [--level 等级] [--q 关键词] [--limit N]
  weather-alert detail <预警ID>
  weather-alert stat
  weather-alert types  列出可选的省份/类型/等级`)
}

func colorLevel(lv string) string {
	if c, ok := levelColors[lv]; ok {
		return c + lv + colorReset
	}
	return lv
}

func main() {
	if len(os.Args) < 2 {
		printUsage()
		return
	}
	var err error
	switch os.Args[1] {
	case "list":
		err = cmdList(os.Args[2:])
	case "detail":
		if len(os.Args) < 3 {
			err = fmt.Errorf("缺少预警 ID，可先 list 查询")
			break
		}
		err = cmdDetail(os.Args[2])
	case "stat":
		err = cmdStat()
	case "types":
		fmt.Println("省份:", strings.Join(provinces, " "))
		fmt.Println("类型:", strings.Join(alertTypes, " "))
		fmt.Println("等级:", strings.Join(alertLevels, " "))
	default:
		printUsage()
	}
	if err != nil {
		fmt.Fprintln(os.Stderr, "错误:", err)
		os.Exit(1)
	}
}

func cmdList(args []string) error {
	province, typ, level, q, limit := "", "", "", "", 30
	for i := 0; i < len(args); i++ {
		switch args[i] {
		case "--province":
			i++
			province = arg(args, &i)
		case "--type":
			i++
			typ = arg(args, &i)
		case "--level":
			i++
			level = arg(args, &i)
		case "--q":
			i++
			q = arg(args, &i)
		case "--limit":
			i++
			limit, _ = strconv.Atoi(arg(args, &i))
		}
	}
	alerts, _, err := fetchAlerts()
	if err != nil {
		return err
	}
	n := 0
	for _, a := range alerts {
		if province != "" && !strings.HasPrefix(a.Location, province) {
			continue
		}
		if typ != "" && !strings.Contains(a.SignalType, typ) {
			continue
		}
		if level != "" && !strings.Contains(a.SignalLevel, level) {
			continue
		}
		if q != "" && !strings.Contains(a.Title, q) {
			continue
		}
		fmt.Printf("%s | %s | %s\n  ID: %s\n",
			colorLevel(a.normalizedLevel()), a.IssueTime, a.Title, a.ID)
		n++
		if n >= limit {
			break
		}
	}
	if n == 0 {
		fmt.Println("没有符合条件的预警信息")
	}
	return nil
}

func arg(args []string, i *int) string {
	if *i < len(args) {
		return args[*i]
	}
	return ""
}

func cmdDetail(id string) error {
	text, err := fetchDetail(id)
	if err != nil {
		return err
	}
	fmt.Println(text)
	fmt.Println("\n原文:", detailURL(id))
	return nil
}

func cmdStat() error {
	_, r, err := fetchAlerts()
	if err != nil {
		return err
	}
	s := r.Data.Stat
	sum := func(levels ...string) int {
		t := 0
		for _, grp := range []string{"province", "city", "county"} {
			for _, lv := range levels {
				t += s[grp][lv]
			}
		}
		return t
	}
	total := sum("r", "o", "y", "b")
	fmt.Printf("预警总数 %d | 红 %d | 橙 %d | 黄 %d | 蓝 %d\n",
		total, sum("r"), sum("o"), sum("y"), sum("b"))
	return nil
}
