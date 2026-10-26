package logsink

import (
	"bytes"
	"log"
	"testing"

	"github.com/orangepro2001/nightdesk/native/internal/proto"
)

func TestSinkRecordsInputsAndText(t *testing.T) {
	var buf bytes.Buffer
	s := New(log.New(&buf, "", 0))
	if err := s.Inject(proto.Input{OpID: 1, Kind: proto.KindMouseMove, X: 10, Y: 20}); err != nil {
		t.Fatal(err)
	}
	if err := s.Inject(proto.Input{OpID: 2, Kind: proto.KindKeyDown, Arg: 0x41}); err != nil {
		t.Fatal(err)
	}
	if err := s.InjectText("你好 NightDesk"); err != nil {
		t.Fatal(err)
	}
	n, last := s.Snapshot()
	if n != 3 {
		t.Fatalf("count = %d", n)
	}
	if !bytes.Contains([]byte(last), []byte("NightDesk")) {
		t.Fatalf("last = %q", last)
	}
	if !bytes.Contains(buf.Bytes(), []byte("TEST INPUT:")) {
		t.Fatal("expected a log line")
	}
}

func TestSinkRejectsUnknownKind(t *testing.T) {
	s := New(nil)
	if err := s.Inject(proto.Input{Kind: 99}); err == nil {
		t.Fatal("expected an error for an unknown input kind")
	}
}
