package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"net/url"
	"strings"
	"text/template"

	"github.com/gomarkdown/markdown"
	"google.golang.org/api/gmail/v1"
)

const appriseMultipartBoundary = "boundary_Dck6sCYmj104WHZsXuwTkU7kfQRq0oLJ"

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
		defaultBody string
		defaultMime string
	)
	switch a.Format {
	case "html":
		defaultMime = "text/html"
		defaultBody = a.Message
	case "markdown":
		defaultMime = "text/html"
		defaultBody = string(markdown.ToHTML([]byte(a.Message), nil, nil))
	default:
		fallthrough
	case "text":
		defaultMime = "text/plain"
		defaultBody = a.Message
	}

	var (
		labelIds = []string{"INBOX"}
		headers  = map[string]string{
			"From":    defaultFrom,
			"To":      "me",
			"Subject": fmt.Sprintf("[%s] %s", a.Type, a.Title),
		}
		body     = defaultBody
		bodyType = defaultMime
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
		if o.BodyType != "" {
			bodyType = o.BodyType
		}
	}

	var sb strings.Builder
	if len(a.Attachments) > 0 {
		// Multipart body with attachments.
		headers["Content-type"] = fmt.Sprintf("multipart/mixed; boundary=\"%s\"", appriseMultipartBoundary)
		for k, v := range headers {
			fmt.Fprintf(&sb, "%s: %s\n", k, v)
		}

		// Write the notification message as the primary body.
		sb.WriteString("\n")
		fmt.Fprintf(&sb, "--%s\n", appriseMultipartBoundary)
		fmt.Fprintf(&sb, "Content-type: %s\n\n", bodyType)
		sb.WriteString(body)
		sb.WriteString("\n")

		// Write the attachments.
		for _, at := range a.Attachments {
			fmt.Fprintf(&sb, "--%s\n", appriseMultipartBoundary)
			fmt.Fprintf(&sb, "Content-type: %s; name=\"%s\"\n", at.Mimetype, url.QueryEscape(at.Filename))
			sb.WriteString("Content-Transfer-Encoding: base64\n\n")
			sb.WriteString(at.Base64)
			sb.WriteString("\n")
		}
		fmt.Fprintf(&sb, "--%s--\n", appriseMultipartBoundary)
	} else {
		// Single-part body.
		if bodyType != "" {
			headers["Content-type"] = bodyType
		}
		for k, v := range headers {
			fmt.Fprintf(&sb, "%s: %s\n", k, v)
		}

		sb.WriteString("\n")
		sb.WriteString(body)
	}

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
			log.Println("Error in appriseToGmail:", err)
			log.Println("Attempted with apprise notification:", a)
			return
		}

		if err := msg.uploadToGmail(mail); err != nil {
			http.Error(w, "Failed request", http.StatusBadRequest)
			log.Println("Gmail upload error:", err)
			log.Println("Attempted with apprise notification:", a)
			return
		}
	})
	log.Fatal(http.ListenAndServe(cfg.Http.Address, nil))
}
