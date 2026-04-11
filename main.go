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
	"strings"

	"github.com/pelletier/go-toml/v2"
	"github.com/tg123/go-htpasswd"
	"golang.org/x/oauth2"
	"golang.org/x/oauth2/google"
	"google.golang.org/api/gmail/v1"
	"google.golang.org/api/googleapi"
	"google.golang.org/api/option"
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

// Global configuration data.
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
		Apprise struct {
			Filters []appriseFilter
		}
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
	if err := c.validateForAuth(); err != nil {
		return err
	}
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
	if c.Htpasswd.File == nil {
		return true
	} else {
		return c.Htpasswd.Match(username, password) && slices.Contains(c.Google.Scopes.Gmail.Insert, username)
	}
}

func (c *config) hasGmailSendScope(username, password string) bool {
	if c.Htpasswd.File == nil {
		return true
	} else {
		return c.Htpasswd.Match(username, password) && slices.Contains(c.Google.Scopes.Gmail.Send, username)
	}
}

// Wrapper for the htpasswd instance. This is necessary to unmarshal it nicely.
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

// Middleware match block from an Apprise endpoint.
type appriseFilter struct {
	Match struct {
		User   string
		Type   string
		Format string
	}
	Output filterOutput
}

// Middleware output block to Gmail data.
type filterOutput struct {
	LabelIds []string
	// Map from header name to templated value.
	Headers map[string]string
	// Templated email body.
	Body               string
	BodyType           string
	InternalDateSource string
	NeverMarkSpam      nullableBool
	ProcessForCalendar nullableBool
	Deleted            nullableBool
}

// Like bool, except with a third "null" state for configuration unmarshaling.
type nullableBool int

func (n *nullableBool) UnmarshalText(b []byte) (err error) {
	if bytes.Equal(b, []byte("true")) {
		*n = 1
	} else if bytes.Equal(b, []byte("false")) {
		*n = -1
	} else {
		*n = 0
	}
	return
}

func (n *nullableBool) isSet() bool {
	return *n != 0
}

func (n *nullableBool) value() bool {
	return *n > 0
}

// Finalized email message ready for submission to Gmail.
type gmailMessage struct {
	LabelIds           []string
	Envelope           string
	InternalDateSource string
	NeverMarkSpam      bool
	ProcessForCalendar bool
	Deleted            bool
}

func (m *gmailMessage) uploadToGmail(mail *gmail.Service) error {
	r, err := mail.Users.Messages.
		Import("me", &gmail.Message{LabelIds: append(m.LabelIds, "UNREAD")}).
		InternalDateSource(m.InternalDateSource).
		NeverMarkSpam(m.NeverMarkSpam).
		ProcessForCalendar(m.ProcessForCalendar).
		Deleted(m.Deleted).
		Media(strings.NewReader(m.Envelope), googleapi.ContentType("message/rfc822")).
		Do()
	if err != nil {
		return err
	}
	if r.HTTPStatusCode != 200 {
		return fmt.Errorf("Gmail returned status code: %v", r.HTTPStatusCode)
	}
	return nil
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
func doListenAndServe(ctx context.Context, cfg *config) {
	if err := cfg.validateForServe(); err != nil {
		log.Fatalln("Error in configuration file:", err)
	}
	if cfg.Htpasswd.File == nil {
		log.Println("WARNING: htpasswd block is not configured. Authentication will be disabled.")
	}

	oa := cfg.readCredentials()

	f, err := os.Open(cfg.Google.Tokens)
	if err != nil {
		log.Fatalln("Failed to open tokens file:", err)
	}

	var tok oauth2.Token
	if err := json.NewDecoder(f).Decode(&tok); err != nil {
		log.Fatalln("Failed to decode tokens file:", err)
	}

	mail, err := gmail.NewService(ctx, option.WithHTTPClient(oa.Client(ctx, &tok)))
	go httpListenAndServe(cfg, mail)
	select {}
}
