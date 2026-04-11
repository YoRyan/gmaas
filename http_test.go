package main

import (
	"io"
	"net/mail"
	"net/url"
	"strings"
	"testing"
)

func TestAppriseFailsWhenEmpty(t *testing.T) {
	_, err := (apprise{}).toGmailMessage("", []appriseFilter{})
	if err == nil {
		t.Error("toGmailMessage() = nil")
	}
}

func TestAppriseDefaultHeaders(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("AzureDiamond", []appriseFilter{})

	if len(gm.LabelIds) != 1 || gm.LabelIds[0] != "INBOX" {
		t.Errorf("gm.LabelIds = %v; want %s", gm.LabelIds, "{INBOX}")
	}

	msg, _ := mail.ReadMessage(strings.NewReader(gm.Envelope))

	if got, want := msg.Header.Get("From"), "AzureDiamond"; got != want {
		t.Errorf("msg.Header.From = %s; want %s", got, want)
	}

	if got, want := msg.Header.Get("To"), "me"; got != want {
		t.Errorf("msg.Header.To = %s; want %s", got, want)
	}

	if got, want := msg.Header.Get("Subject"), "[info] Hello, World!"; got != want {
		t.Errorf("msg.Header.Subject = %s; want %s", got, want)
	}
}

func TestAppriseDefaultBody(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("AzureDiamond", []appriseFilter{})
	msg, _ := mail.ReadMessage(strings.NewReader(gm.Envelope))
	body, _ := io.ReadAll(msg.Body)

	if got, want := string(body), "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."; got != want {
		t.Errorf("msg.Body = %s; want %s", got, want)
	}
}
func TestAppriseDefaultParameters(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("AzureDiamond", []appriseFilter{})

	if got, want := gm.InternalDateSource, "receivedTime"; got != want {
		t.Errorf("gm.InternalDateSource = %s; want %s", got, want)
	}
	if got, want := gm.NeverMarkSpam, true; got != want {
		t.Errorf("gm.NeverMarkSpam = %v; want %v", got, want)
	}
	if got, want := gm.ProcessForCalendar, false; got != want {
		t.Errorf("gm.ProcessForCalendar = %v; want %v", got, want)
	}
	if got, want := gm.Deleted, false; got != want {
		t.Errorf("gm.Deleted = %v; want %v", got, want)
	}
}

func TestAppriseDefaultHeadersUnauthenticated(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("", []appriseFilter{})
	msg, _ := mail.ReadMessage(strings.NewReader(gm.Envelope))

	if got, want := msg.Header.Get("From"), "me"; got != want {
		t.Errorf("msg.Header.From = %s; want %s", got, want)
	}
}

func TestAppriseFilterMatchesUser(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("AzureDiamond", []appriseFilter{
		{
			Match: struct {
				User   string
				Type   string
				Format string
			}{User: "AzureDiamond"},
			Output: filterOutput{
				LabelIds: []string{"STARRED"},
			},
		},
	})

	if got, want := len(gm.LabelIds), 1; got != want {
		t.Errorf("len(gm.LabelIds) = %v; want %v", got, want)
	}
	if got, want := gm.LabelIds[0], "STARRED"; got != want {
		t.Errorf("gm.LabelIds[0] = %s; want %s", got, want)
	}
}

func TestAppriseFilterMatchesType(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("AzureDiamond", []appriseFilter{
		{
			Match: struct {
				User   string
				Type   string
				Format string
			}{Type: "info"},
			Output: filterOutput{
				LabelIds: []string{"STARRED"},
			},
		},
	})

	if got, want := len(gm.LabelIds), 1; got != want {
		t.Errorf("len(gm.LabelIds) = %v; want %v", got, want)
	}
	if got, want := gm.LabelIds[0], "STARRED"; got != want {
		t.Errorf("gm.LabelIds[0] = %s; want %s", got, want)
	}
}

func TestAppriseFilterMatchesFormat(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("AzureDiamond", []appriseFilter{
		{
			Match: struct {
				User   string
				Type   string
				Format string
			}{Type: "info"},
			Output: filterOutput{
				LabelIds: []string{"STARRED"},
			},
		},
	})

	if got, want := len(gm.LabelIds), 1; got != want {
		t.Errorf("len(gm.LabelIds) = %v; want %v", got, want)
	}
	if got, want := gm.LabelIds[0], "STARRED"; got != want {
		t.Errorf("gm.LabelIds[0] = %s; want %s", got, want)
	}
}

func TestAppriseFilterSetsBody(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("", []appriseFilter{
		{
			Match: struct {
				User   string
				Type   string
				Format string
			}{Type: "info"},
			Output: filterOutput{
				Body: "{{.Title}}",
			},
		},
	})
	msg, _ := mail.ReadMessage(strings.NewReader(gm.Envelope))
	body, _ := io.ReadAll(msg.Body)

	if got, want := string(body), "Hello, World!"; got != want {
		t.Errorf("gm.Envelope.Body = %s; want %s", got, want)
	}
}

func TestAppriseFilterSetsBodyType(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("", []appriseFilter{
		{
			Match: struct {
				User   string
				Type   string
				Format string
			}{Type: "info"},
			Output: filterOutput{
				Body:     "<p>This is my message: <strong>{{.Title}}</strong></p>",
				BodyType: "text/html",
			},
		},
	})
	msg, _ := mail.ReadMessage(strings.NewReader(gm.Envelope))
	body, _ := io.ReadAll(msg.Body)

	if got, want := string(body), "<p>This is my message: <strong>Hello, World!</strong></p>"; got != want {
		t.Errorf("gm.Envelope.Body = %s; want %s", got, want)
	}
	if got, want := msg.Header.Get("Content-Type"), "text/html"; got != want {
		t.Errorf("gm.Envelope.Header.Content-Type = %s; want %s", got, want)
	}
}

func TestAppriseFilterSetsHeader(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("", []appriseFilter{
		{
			Match: struct {
				User   string
				Type   string
				Format string
			}{Type: "info"},
			Output: filterOutput{
				Headers: map[string]string{
					"Subject": "[Awesome] {{.Title}}",
				},
			},
		},
	})
	msg, _ := mail.ReadMessage(strings.NewReader(gm.Envelope))

	if got, want := msg.Header.Get("Subject"), "[Awesome] Hello, World!"; got != want {
		t.Errorf("msg.Header.Subject = %s; want %s", got, want)
	}
}

func TestAppriseFilterSetsDateSource(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("", []appriseFilter{
		{
			Match: struct {
				User   string
				Type   string
				Format string
			}{Type: "info"},
			Output: filterOutput{
				Headers: map[string]string{
					"Date": "Wed, 1 Apr 2026 22:54:15 +0000",
				},
				InternalDateSource: "dateHeader",
			},
		},
	})
	msg, _ := mail.ReadMessage(strings.NewReader(gm.Envelope))

	if got, want := msg.Header.Get("Date"), "Wed, 1 Apr 2026 22:54:15 +0000"; got != want {
		t.Errorf("msg.Header.Date = %s; want %s", got, want)
	}
	if got, want := gm.InternalDateSource, "dateHeader"; got != want {
		t.Errorf("gm.InternalDateSource = %s; want %s", got, want)
	}
}

func TestAppriseFilterSetsNullableParameters(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("", []appriseFilter{
		{
			Match: struct {
				User   string
				Type   string
				Format string
			}{Type: "info"},
			Output: filterOutput{
				NeverMarkSpam:      -1,
				ProcessForCalendar: 1,
				Deleted:            1,
			},
		},
	})

	if got, want := gm.NeverMarkSpam, false; got != want {
		t.Errorf("gm.NeverMarkSpam = %v; want %v", got, want)
	}
	if got, want := gm.ProcessForCalendar, true; got != want {
		t.Errorf("gm.ProcessForCalendar = %v; want %v", got, want)
	}
	if got, want := gm.Deleted, true; got != want {
		t.Errorf("gm.Deleted = %v; want %v", got, want)
	}
}

func TestAppriseFilterOverridesPrevious(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("AzureDiamond", []appriseFilter{
		{
			Match: struct {
				User   string
				Type   string
				Format string
			}{User: "AzureDiamond"},
			Output: filterOutput{
				Headers: map[string]string{
					"Subject": "[Awesome] {{.Title}}",
				},
			},
		},
		{
			Match: struct {
				User   string
				Type   string
				Format string
			}{Type: "info"},
			Output: filterOutput{
				Headers: map[string]string{
					"Subject": "[Bad] {{.Title}}",
				},
			},
		},
	})
	msg, _ := mail.ReadMessage(strings.NewReader(gm.Envelope))

	if got, want := msg.Header.Get("Subject"), "[Bad] Hello, World!"; got != want {
		t.Errorf("msg.Header.Subject = %s; want %s", got, want)
	}
}

func TestAppriseFilterInheritsPrevious(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}).toGmailMessage("AzureDiamond", []appriseFilter{
		{
			Match: struct {
				User   string
				Type   string
				Format string
			}{User: "AzureDiamond"},
			Output: filterOutput{
				Headers: map[string]string{
					"Subject": "[Awesome] {{.Title}}",
				},
			},
		},
		{
			Match: struct {
				User   string
				Type   string
				Format string
			}{Type: "info"},
			Output: filterOutput{
				LabelIds: []string{"STARRED"},
			},
		},
	})
	msg, _ := mail.ReadMessage(strings.NewReader(gm.Envelope))

	if got, want := len(gm.LabelIds), 1; got != want {
		t.Errorf("len(gm.LabelIds) = %v; want %v", got, want)
	}
	if got, want := gm.LabelIds[0], "STARRED"; got != want {
		t.Errorf("gm.LabelIds[0] = %s; want %s", got, want)
	}
	if got, want := msg.Header.Get("Subject"), "[Awesome] Hello, World!"; got != want {
		t.Errorf("msg.Header.Subject = %s; want %s", got, want)
	}
}

func TestAppriseConvertsMarkdown(t *testing.T) {
	gm, _ := (apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "# Hello, World!\n\n**Lorem ipsum** dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "markdown",
		Type:    "info",
	}).toGmailMessage("", []appriseFilter{})
	msg, _ := mail.ReadMessage(strings.NewReader(gm.Envelope))
	body, _ := io.ReadAll(msg.Body)

	// Comparing HTML like this can be fragile. Hopefully it won't break in the far future...
	if got, want := strings.TrimSpace(string(body)), "<h1>Hello, World!</h1>\n\n<p><strong>Lorem ipsum</strong> dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>"; got != want {
		t.Errorf("gm.Envelope.Body = %s; want %s", got, want)
	}
	if got, want := msg.Header.Get("Content-Type"), "text/html"; got != want {
		t.Errorf("gm.Envelope.Header.Content-Type = %s; want %s", got, want)
	}
}

// (This test isn't terribly thorough as I'm not interested in writing a multipart parser.)
func TestAppriseSendsAttachments(t *testing.T) {
	attachments := []struct {
		Filename string
		Base64   string
		Mimetype string
	}{
		{Filename: "hello.txt", Base64: "SGVsbG8sIFdvcmxkIQ==", Mimetype: "text/plain"},
		{Filename: "lorem.json", Base64: "eyJtZXNzYWdlIjoiTG9yZW0gaXBzdW0gZG9sb3Igc2l0IGFtZXQsIGNvbnNlY3RldHVyIGFkaXBpc2NpbmcgZWxpdCwgc2VkIGRvIGVpdXNtb2QgdGVtcG9yIGluY2lkaWR1bnQgdXQgbGFib3JlIGV0IGRvbG9yZSBtYWduYSBhbGlxdWEuIn0=", Mimetype: "application/json"},
	}
	gm, _ := (apprise{
		Version:     "1.0",
		Title:       "Hello, World!",
		Message:     "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:      "text",
		Attachments: attachments,
		Type:        "info",
	}).toGmailMessage("", []appriseFilter{})
	msg, _ := mail.ReadMessage(strings.NewReader(gm.Envelope))
	body, _ := io.ReadAll(msg.Body)
	s := string(body)

	if got, want := strings.Contains(s, "Lorem ipsum dolor sit amet"), true; got != want {
		t.Errorf("strings.Contains(s, Lorem ipsum dolor sit amet) = %v; want %v", got, want)
	}
	if got, want := strings.Contains(s, url.QueryEscape("hello.txt")), true; got != want {
		t.Errorf("strings.Contains(s, hello.txt) = %v; want %v", got, want)
	}
	if got, want := strings.Contains(s, "SGVsbG8sIFdvcmxkIQ=="), true; got != want {
		t.Errorf("strings.Contains(s, base64(hello.txt)) = %v; want %v", got, want)
	}
	if got, want := strings.Contains(s, url.QueryEscape("lorem.json")), true; got != want {
		t.Errorf("strings.Contains(s, lorem.json) = %v; want %v", got, want)
	}
	if got, want := strings.Contains(s, "eyJtZXNzYWdlIjoiTG9yZW0gaXBzdW0gZG9sb3Igc2l0IGFtZXQsIGNvbnNlY3RldHVyIGFkaXBpc2NpbmcgZWxpdCwgc2VkIGRvIGVpdXNtb2QgdGVtcG9yIGluY2lkaWR1bnQgdXQgbGFib3JlIGV0IGRvbG9yZSBtYWduYSBhbGlxdWEuIn0="), true; got != want {
		t.Errorf("strings.Contains(s, base64(lorem.json)) = %v; want %v", got, want)
	}
}
