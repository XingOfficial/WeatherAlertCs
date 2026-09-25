// 账号与收藏云同步（login / signup / sendcode / fav / whoami）
package main

import (
	"bytes"

	"encoding/json"
	"fmt"
	"io"

	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strings"
)

const DefaultServer = "https://xingclouddisk.share.zrok.io/weather-alert-web"

type appConfig struct {
	Server string `json:"server"`
	Token  string `json:"token"`
	User   string `json:"user"`
}

func configPath() string { return filepath.Join(os.Getenv("HOME"), ".weather-alert-go.json") }

func loadConfig() appConfig {
	var c appConfig
	if data, err := os.ReadFile(configPath()); err == nil {
		_ = json.Unmarshal(data, &c)
	}
	return c
}

func saveConfig(c appConfig) {
	_ = os.MkdirAll(filepath.Dir(configPath()), 0700)
	data, _ := json.MarshalIndent(c, "", "  ")
	_ = os.WriteFile(configPath(), data, 0600)
}

func favFilePath() string { return filepath.Join(os.Getenv("HOME"), ".weather-alert-go-favs.json") }

func loadLocalFavs() []string {
	var ids []string
	if data, err := os.ReadFile(favFilePath()); err == nil {
		_ = json.Unmarshal(data, &ids)
	}
	return ids
}

func saveLocalFavs(ids []string) {
	seen := map[string]bool{}
	var out []string
	for _, id := range ids {
		if id != "" && !seen[id] {
			seen[id] = true
			out = append(out, id)
		}
	}
	data, _ := json.MarshalIndent(out, "", "  ")
	_ = os.WriteFile(favFilePath(), data, 0600)
}

func apiBase(c appConfig) string {
	if s := os.Getenv("WA_SERVER"); s != "" {
		return strings.TrimRight(s, "/")
	}
	if c.Server != "" {
		return strings.TrimRight(c.Server, "/")
	}
	return DefaultServer
}

// parseFlags: 同时支持 --long 值 / -s 值 / --long=值 三种写法
func parseFlags(args []string) map[string]string {
	short := map[string]string{
		"u": "username", "n": "name", "p": "password",
		"e": "email", "a": "authcode", "t": "signuptype", "s": "server",
	}
	m := map[string]string{}
	for i := 0; i < len(args); i++ {
		a := args[i]
		if !strings.HasPrefix(a, "-") {
			continue
		}
		var k, v string
		if idx := strings.Index(a, "="); idx > 0 {
			k, v = a[:idx], a[idx+1:]
		} else {
			k = a
			if i+1 < len(args) {
				i++
				v = args[i]
			}
		}
		k = strings.TrimLeft(k, "-")
		if long, ok := short[k]; ok {
			k = long
		}
		if v != "" {
			m[k] = v
		}
	}
	return m
}

func httpPostForm(rawurl string, form url.Values) ([]byte, error) {
	req, err := http.NewRequest("POST", rawurl, strings.NewReader(form.Encode()))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	req.Header.Set("User-Agent", "Mozilla/5.0 (Linux; Android 13) Chrome/120 Mobile")
	resp, err := client.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	return io.ReadAll(resp.Body)
}

func httpPostJSON(rawurl string, body any) ([]byte, error) {
	data, _ := json.Marshal(body)
	req, err := http.NewRequest("POST", rawurl, bytes.NewReader(data))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/json")
	resp, err := client.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	return io.ReadAll(resp.Body)
}

func apiDecode(data []byte) (map[string]any, error) {
	var m map[string]any
	if err := json.Unmarshal(data, &m); err != nil {
		return nil, fmt.Errorf("接口返回格式异常")
	}
	if ok, _ := m["ok"].(bool); !ok {
		msg, _ := m["error"].(string)
		return nil, fmt.Errorf("%s", msg)
	}
	return m, nil
}

func cmdSignup(args []string) error {
	f := parseFlags(args)
	t := f["signuptype"]
	if t == "" {
		t = "namepassword"
	}
	form := url.Values{"action": {"signup"}, "type": {t}}
	switch t {
	case "emailauthcode":
		if f["email"] == "" || f["authcode"] == "" {
			return fmt.Errorf("用法: signup -t emailauthcode -e 邮箱 -a 验证码（先 sendcode -e 邮箱 获取验证码）")
		}
		form.Set("email", f["email"])
		form.Set("authcode", f["authcode"])
	default:
		if f["name"] == "" || f["password"] == "" {
			return fmt.Errorf("用法: signup -t namepassword -n 用户名 -p 密码")
		}
		form.Set("name", f["name"])
		form.Set("password", f["password"])
	}
	data, err := httpPostForm(apiBase(loadConfig())+"/api/account.php", form)
	if err != nil {
		return err
	}
	m, err := apiDecode(data)
	if err != nil {
		return err
	}
	cfg := loadConfig()
	if f["server"] != "" {
		cfg.Server = f["server"]
	}
	cfg.Token, _ = m["token"].(string)
	cfg.User, _ = m["user"].(string)
	saveConfig(cfg)
	fmt.Printf("注册成功，已登录为 %s（token 已保存，收藏自动云同步）\n", cfg.User)
	return syncFavs(cfg)
}

func cmdLogin(args []string) error {
	f := parseFlags(args)
	form := url.Values{"action": {"login"}}
	if f["email"] != "" && f["authcode"] != "" {
		form.Set("email", f["email"])
		form.Set("authcode", f["authcode"])
	} else if f["username"] != "" && f["password"] != "" {
		form.Set("name", f["username"])
		form.Set("password", f["password"])
	} else {
		return fmt.Errorf("用法: login -u 用户名 -p 密码，或 login -e 邮箱 -a 验证码")
	}
	data, err := httpPostForm(apiBase(loadConfig())+"/api/account.php", form)
	if err != nil {
		return err
	}
	m, err := apiDecode(data)
	if err != nil {
		return err
	}
	cfg := loadConfig()
	if f["server"] != "" {
		cfg.Server = f["server"]
	}
	cfg.Token, _ = m["token"].(string)
	cfg.User, _ = m["user"].(string)
	saveConfig(cfg)
	fmt.Printf("登录成功：%s（token 已保存，收藏自动云同步）\n", cfg.User)
	return syncFavs(cfg)
}

func cmdSendcode(args []string) error {
	f := parseFlags(args)
	if f["email"] == "" {
		return fmt.Errorf("用法: sendcode -e 邮箱")
	}
	data, err := httpPostForm(apiBase(loadConfig())+"/api/account.php",
		url.Values{"action": {"sendcode"}, "email": {f["email"]}})
	if err != nil {
		return err
	}
	m, err := apiDecode(data)
	if err != nil {
		return err
	}
	if sent, _ := m["sent"].(bool); sent {
		fmt.Println("验证码已发送至邮箱，10 分钟内有效")
	} else {
		// 服务器无法直接发邮件时回显验证码（个人部署兜底）
		fmt.Printf("邮件通道不可用，验证码: %v（10 分钟内有效）\n", m["debug_code"])
	}
	return nil
}

func cmdFav(args []string) error {
	cfg := loadConfig()
	if len(args) == 0 {
		return fmt.Errorf("用法: fav add <ID> | fav del <ID> | fav list | fav sync")
	}
	favs := loadLocalFavs()
	switch args[0] {
	case "add":
		if len(args) < 2 {
			return fmt.Errorf("缺少预警 ID")
		}
		favs = append(favs, args[1])
		saveLocalFavs(favs)
		fmt.Printf("已收藏 %s（本地 %d 条）\n", args[1], len(favs))
	case "del":
		if len(args) < 2 {
			return fmt.Errorf("缺少预警 ID")
		}
		var out []string
		for _, id := range favs {
			if id != args[1] {
				out = append(out, id)
			}
		}
		saveLocalFavs(out)
		fmt.Printf("已取消收藏 %s（本地 %d 条）\n", args[1], len(out))
	case "list":
		if len(favs) == 0 {
			fmt.Println("本地暂无收藏")
			return nil
		}
		for _, id := range favs {
			fmt.Println(id)
		}
		fmt.Printf("共 %d 条\n", len(favs))
	case "sync":
		return syncFavs(cfg)
	default:
		return fmt.Errorf("未知子命令: %s（可选 add/del/list/sync）", args[0])
	}
	if cfg.Token != "" {
		return syncFavs(cfg)
	}
	return nil
}

// syncFavs: 本地收藏与账号云端 union 合并（需要已登录）
func syncFavs(cfg appConfig) error {
	if cfg.Token == "" {
		fmt.Println("提示: 尚未登录，收藏仅在本地（login 后自动云同步）")
		return nil
	}
	urls := []string{apiBase(cfg) + "/api/userfavs", apiBase(cfg) + "/api/userfavs.php"}
	var m map[string]any
	var lastErr error
	for _, u := range urls {
		data, err := httpPostJSON(u, map[string]any{
			"token": cfg.Token, "ids": loadLocalFavs(),
		})
		if err != nil {
			return err
		}
		m, lastErr = apiDecode(data)
		if lastErr == nil {
			break
		}
	}
	if lastErr != nil {
		return lastErr
	}
	raw, _ := m["favs"].([]any)
	var merged []string
	for _, v := range raw {
		if s, ok := v.(string); ok {
			merged = append(merged, s)
		}
	}
	saveLocalFavs(merged)
	fmt.Printf("收藏云同步完成，共 %d 条（账号 %s）\n", len(merged), cfg.User)
	return nil
}

func cmdWhoami() error {
	cfg := loadConfig()
	if cfg.User == "" {
		fmt.Println("未登录（login 或 signup 后生效）")
		return nil
	}
	fmt.Printf("当前账号: %s | 服务器: %s | 本地收藏 %d 条\n",
		cfg.User, apiBase(cfg), len(loadLocalFavs()))
	return nil
}
