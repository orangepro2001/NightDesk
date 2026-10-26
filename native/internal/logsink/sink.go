// Package logsink implements remote.Injector without touching the operating
// system. It exists for safe end-to-end tests of the real host binary: input is
// decoded, validated and acknowledged exactly as in production, but nothing is
// sent to the desktop.
package logsink

import (
	"fmt"
	"log"
	"sync"

	"github.com/orangepro2001/nightdesk/native/internal/proto"
)

// Sink records every injected action and logs a one-line description.
type Sink struct {
	mu   sync.Mutex
	log  *log.Logger
	n    int
	last string
}

// New returns a sink that writes to logger. A nil logger discards the lines.
func New(logger *log.Logger) *Sink { return &Sink{log: logger} }

// Inject implements remote.Injector.
func (s *Sink) Inject(in proto.Input) error {
	desc := fmt.Sprintf("op=%d kind=%d x=%d y=%d arg=%d", in.OpID, in.Kind, in.X, in.Y, in.Arg)
	if in.Kind < proto.KindMouseMove || in.Kind > proto.KindKeyUp {
		return fmt.Errorf("logsink: unknown input kind %d", in.Kind)
	}
	s.record("input " + desc)
	return nil
}

// InjectText implements remote.TextInjector.
func (s *Sink) InjectText(text string) error {
	s.record(fmt.Sprintf("text %q", text))
	return nil
}

// Snapshot reports how many actions were accepted and the last description.
func (s *Sink) Snapshot() (int, string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.n, s.last
}

func (s *Sink) record(desc string) {
	s.mu.Lock()
	s.n++
	s.last = desc
	logger := s.log
	s.mu.Unlock()
	if logger != nil {
		logger.Printf("TEST INPUT: %s", desc)
	}
}
