package main

import (
	"io"
	"net/mail"
	"strings"
	"testing"
)

func TestAppriseDefaultHeaders(t *testing.T) {
	gm, _ := appriseToGmail(apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}, "AzureDiamond", []appriseFilter{})

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

func TestAppriseDefaultHeadersUnauthenticated(t *testing.T) {
	gm, _ := appriseToGmail(apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}, "", []appriseFilter{})
	msg, _ := mail.ReadMessage(strings.NewReader(gm.Envelope))

	if got, want := msg.Header.Get("From"), "me"; got != want {
		t.Errorf("msg.Header.From = %s; want %s", got, want)
	}
}

func TestAppriseFilterMatchesUser(t *testing.T) {
	gm, _ := appriseToGmail(apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}, "AzureDiamond", []appriseFilter{
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
	gm, _ := appriseToGmail(apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}, "AzureDiamond", []appriseFilter{
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
	gm, _ := appriseToGmail(apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}, "AzureDiamond", []appriseFilter{
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
	gm, _ := appriseToGmail(apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}, "", []appriseFilter{
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

	if got, want := strings.TrimSpace(string(body)), "Hello, World!"; got != want {
		t.Errorf("gm.Envelope.Body = %s; want %s", got, want)
	}
}

func TestAppriseFilterSetsHeader(t *testing.T) {
	gm, _ := appriseToGmail(apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}, "", []appriseFilter{
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

func TestAppriseFilterOverridesPrevious(t *testing.T) {
	gm, _ := appriseToGmail(apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}, "AzureDiamond", []appriseFilter{
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
	gm, _ := appriseToGmail(apprise{
		Version: "1.0",
		Title:   "Hello, World!",
		Message: "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		Format:  "text",
		Type:    "info",
	}, "AzureDiamond", []appriseFilter{
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
