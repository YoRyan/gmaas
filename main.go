package main

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"log"
	"net/url"
	"os"
	"slices"

	"github.com/pelletier/go-toml/v2"
	"github.com/tg123/go-htpasswd"
	"golang.org/x/oauth2"
	"golang.org/x/oauth2/google"
	"google.golang.org/api/gmail/v1"
)

func main() {
	ctx := context.Background()

	var (
		doAuth     bool
		configPath string
	)
	flag.BoolVar(&doAuth, "auth", false, "Request access and refresh tokens from Google instead of running the service, and write the tokens to the tokens path.")
	flag.StringVar(&configPath, "config", "", "Path to the configuration file.")
	flag.Parse()

	if configPath == "" {
		log.Fatalln("Missing path to configuration file.")
	}

	var cfg config
	b, err := os.ReadFile(configPath)
	if err != nil {
		log.Fatalln(err)
	}

	if err := toml.Unmarshal(b, &cfg); err != nil {
		log.Fatalln(err)
	}

	if doAuth {
		doRequestAuth(ctx, &cfg)
	} else {
		doListenAndServe(ctx, &cfg)
	}
}

type config struct {
	Htpasswd users
	Google   struct {
		Credentials string
		Tokens      string
		Scopes      struct {
			Gmail struct {
				Insert []string
				Send   []string
			}
		}
	}
	Http struct {
		Address string
	}
	Smtp struct {
		Address string
	}
}

func (c *config) validateForAuth() error {
	if c.Google.Credentials == "" {
		return errors.New("Missing path to Google credentials file.")
	}
	if c.Google.Tokens == "" {
		return errors.New("Missing path to Google tokens file.")
	}
	return nil
}

func (c *config) validateForServe() error {
	if c.Http.Address == "" && c.Smtp.Address == "" {
		return errors.New("No HTTP or SMTP listener is configured. There is nothing to do.")
	}
	return nil
}

func (c *config) readCredentials() (oa *oauth2.Config) {
	b, err := os.ReadFile(c.Google.Credentials)
	if err != nil {
		log.Fatalln(err)
	}

	oa, err = google.ConfigFromJSON(b, gmail.GmailInsertScope, gmail.GmailSendScope)
	if err != nil {
		log.Fatalln(err)
	}
	return
}

func (c *config) hasGmailInsertScope(username, password string) bool {
	return c.Htpasswd.Match(username, password) && slices.Contains(c.Google.Scopes.Gmail.Insert, username)
}

func (c *config) hasGmailSendScope(username, password string) bool {
	return c.Htpasswd.Match(username, password) && slices.Contains(c.Google.Scopes.Gmail.Send, username)
}

type users struct {
	*htpasswd.File
}

func (u *users) UnmarshalText(b []byte) (err error) {
	badLineHandler := func(err error) {
		log.Println("Bad line in htpasswd block:", err)
	}
	u.File, err = htpasswd.NewFromReader(bytes.NewReader(b), htpasswd.DefaultSystems, badLineHandler)
	return
}

// Run in request tokens mode.
func doRequestAuth(ctx context.Context, cfg *config) {
	if err := cfg.validateForAuth(); err != nil {
		log.Fatalln("Error in configuration file:", err)
	}

	oa := cfg.readCredentials()

	authURL := oa.AuthCodeURL("", oauth2.AccessTypeOffline)
	fmt.Println("Navigate to the following URL in your browser:")
	fmt.Println(authURL)

	var redirectURL string
	fmt.Println("")
	fmt.Println("Once you've authorized the request, your browser will redirect to an http://localhost URL that will fail to load. Paste the entire URL here:")
	if _, err := fmt.Scan(&redirectURL); err != nil {
		log.Fatalln("Unable to read redirected URL:", err)
	}

	parsedURL, err := url.Parse(redirectURL)
	if err != nil {
		log.Fatalln("Unable to parse redirected URL:", err)
	}

	tokens, err := oa.Exchange(ctx, parsedURL.Query().Get("code"))
	if err != nil {
		log.Fatalln("Unable to retrieve tokens from Google:", err)
	}

	fmt.Println("")
	fmt.Println("Your access and refresh tokens:")
	b, err := json.Marshal(tokens)
	if err != nil {
		log.Fatalln("Error converting tokens to JSON:", err)
	}

	if _, err := os.Stdout.Write(b); err != nil {
		log.Fatalln(err)
	}
	fmt.Println("")

	path := cfg.Google.Tokens
	if err := os.WriteFile(path, b, 0600); err != nil {
		log.Fatalln("Error writing tokens to output file:", err)
	}
	fmt.Println("")
	fmt.Println("Tokens successfully saved to ", path, ".")
}

// Run in listen-and-serve mode.
func doListenAndServe(_ context.Context, cfg *config) {
	if err := cfg.validateForServe(); err != nil {
		log.Fatalln("Error in configuration file:", err)
	}
	if cfg.Htpasswd.File == nil {
		log.Println("WARNING: htpasswd block is not configured. Authentication will be disabled.")
	}

	fmt.Println(cfg)
}
