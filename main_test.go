package main

import (
	"fmt"
	"strings"
	"testing"

	"github.com/tg123/go-htpasswd"
)

const (
	htpasswdPassword = "hunter2"
	htpasswdHash     = "$apr1$nKTVHFsh$8gVerNz4iYOp211EbpBpJ0"
)

func makeHtpasswd(t *testing.T, users []string) (u users) {
	var sb strings.Builder
	for _, user := range users {
		fmt.Fprintf(&sb, "%s:%s\n", user, htpasswdHash)
	}

	var err error
	if u.File, err = htpasswd.NewFromReader(strings.NewReader(sb.String()), htpasswd.DefaultSystems, func(_ error) {}); err != nil {
		t.Error(err)
	}

	return
}

func TestAuthConfigMissingCreds(t *testing.T) {
	cfg := &config{}
	cfg.Google.Tokens = "tokens.json"
	if err := cfg.validateForAuth(); err == nil {
		t.Error("validateForAuth() = nil")
	}
}

func TestAuthConfigMissingTokens(t *testing.T) {
	cfg := &config{}
	cfg.Google.Credentials = "credentials.json"
	if err := cfg.validateForAuth(); err == nil {
		t.Error("validateForAuth() = nil")
	}
}
func TestAuthConfigIsValid(t *testing.T) {
	cfg := &config{}
	cfg.Google.Credentials = "credentials.json"
	cfg.Google.Tokens = "tokens.json"
	if err := cfg.validateForAuth(); err != nil {
		t.Error("validateForAuth() != nil")
	}
}

func TestServeConfigMissingAddresses(t *testing.T) {
	cfg := &config{}
	if err := cfg.validateForServe(); err == nil {
		t.Error("validateForServe() = nil")
	}
}

func TestServeConfigNoAuthHttp(t *testing.T) {
	cfg := &config{}
	cfg.Google.Credentials = "credentials.json"
	cfg.Google.Tokens = "tokens.json"
	cfg.Http.Address = "[::1]:8080"
	if err := cfg.validateForServe(); err != nil {
		t.Error("validateForServe() != nil")
	}
}

func TestServeConfigNoAuthSmtp(t *testing.T) {
	cfg := &config{}
	cfg.Google.Credentials = "credentials.json"
	cfg.Google.Tokens = "tokens.json"
	cfg.Smtp.Address = "[::1]:1025"
	if err := cfg.validateForServe(); err != nil {
		t.Error("validateForServe() != nil")
	}
}

func TestGmailInsertNotAuthorized(t *testing.T) {
	cfg := &config{}
	cfg.Htpasswd = makeHtpasswd(t, []string{"AzureDiamond"})
	if got := cfg.hasGmailInsertScope("AzureDiamond", htpasswdPassword); got {
		t.Errorf("hasGmailInsertScope() = %v; want %v", got, false)
	}
}

func TestGmailInsertAuthorized(t *testing.T) {
	cfg := &config{}
	cfg.Google.Scopes.Gmail.Insert = []string{"AzureDiamond"}
	cfg.Htpasswd = makeHtpasswd(t, []string{"AzureDiamond"})
	if got := cfg.hasGmailInsertScope("AzureDiamond", htpasswdPassword); !got {
		t.Errorf("hasGmailInsertScope() = %v; want %v", got, true)
	}
}

func TestGmailSendNotAuthorized(t *testing.T) {
	cfg := &config{}
	cfg.Htpasswd = makeHtpasswd(t, []string{"AzureDiamond"})
	if got := cfg.hasGmailSendScope("AzureDiamond", htpasswdPassword); got {
		t.Errorf("hasGmailSendScope() = %v; want %v", got, false)
	}
}

func TestGmailSendAuthorized(t *testing.T) {
	cfg := &config{}
	cfg.Google.Scopes.Gmail.Send = []string{"AzureDiamond"}
	cfg.Htpasswd = makeHtpasswd(t, []string{"AzureDiamond"})
	if got := cfg.hasGmailSendScope("AzureDiamond", htpasswdPassword); !got {
		t.Errorf("hasGmailSendScope() = %v; want %v", got, true)
	}
}
