package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"strings"
	"text/template"

	"google.golang.org/api/gmail/v1"
)

type apprise struct {
	Version string
	Title   string
	Message string
	// Not an official field. Allows sender to specify "text", "html", or "markdown".
	Format      string
	Attachments []struct {
		Filename string
		Base64   string
		Mimetype string
	}
	Type string
}

func appriseToGmail(a apprise, user string, filters []appriseFilter) (msg gmailMessage, err error) {
	var defaultFrom string
	if user == "" {
		defaultFrom = "me"
	} else {
		defaultFrom = user
	}

	var (
		labelIds = []string{"INBOX"}
		headers  = map[string]string{
			"From":    defaultFrom,
			"To":      "me",
			"Subject": fmt.Sprintf("[%s] %s", a.Type, a.Title),
		}
		body string = a.Message
	)

	for _, f := range filters {
		m := f.Match
		if m.User != "" && m.User != user {
			continue
		}
		if m.Type != "" && m.Type != a.Type {
			continue
		}
		if m.Format != "" && m.Format != a.Format {
			continue
		}

		o := f.Output
		if len(o.LabelIds) > 0 {
			labelIds = o.LabelIds
		}
		if len(o.Headers) > 0 {
			for k, v := range o.Headers {
				tmpl, err := template.New(k).Parse(v)
				if err != nil {
					return msg, fmt.Errorf("Failed to parse template: %v", err)
				}

				var b bytes.Buffer
				if err := tmpl.Execute(&b, a); err != nil {
					return msg, fmt.Errorf("Failed to execute template: %v", err)
				}

				headers[k] = b.String()
			}
		}
		if o.Body != "" {
			tmpl, err := template.New("body").Parse(o.Body)
			if err != nil {
				return msg, fmt.Errorf("Failed to parse template: %v", err)
			}

			var b bytes.Buffer
			if err := tmpl.Execute(&b, a); err != nil {
				return msg, fmt.Errorf("Failed to execute template: %v", err)
			}

			body = b.String()
		}
	}

	var sb strings.Builder
	for k, v := range headers {
		fmt.Fprintf(&sb, "%s: %s\n", k, v)
	}
	sb.WriteString("\n\n")
	sb.WriteString(body)

	msg.LabelIds = labelIds
	msg.Envelope = sb.String()

	return
}

func httpListenAndServe(cfg *config, mail *gmail.Service) {
	http.HandleFunc("/apprise/json", func(w http.ResponseWriter, r *http.Request) {
		user, pass, _ := r.BasicAuth()
		if !cfg.hasGmailInsertScope(user, pass) {
			http.Error(w, "Not authorized", http.StatusUnauthorized)
			return
		}

		if r.Method != "POST" {
			http.Error(w, "POST request required", http.StatusBadRequest)
			return
		}

		var a apprise
		if err := json.NewDecoder(r.Body).Decode(&a); err != nil {
			http.Error(w, "Invalid JSON", http.StatusBadRequest)
			return
		}

		msg, err := appriseToGmail(a, user, cfg.Http.Apprise.Filters)
		if err != nil {
			http.Error(w, "Failed request", http.StatusBadRequest)
			return
		}

		if err := msg.uploadToGmail(mail); err != nil {
			log.Println("Gmail upload error:", err)
			log.Println("Attempted with apprise notification:", a)
			http.Error(w, "Failed request", http.StatusBadRequest)
			return
		}
	})
	log.Fatal(http.ListenAndServe(cfg.Http.Address, nil))
}
