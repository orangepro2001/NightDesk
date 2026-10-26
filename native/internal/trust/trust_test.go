package trust

import (
	"path/filepath"
	"strings"
	"testing"
)

func TestIdentityIsStable(t *testing.T) {
	dir := t.TempDir()
	a, err := Identity(dir)
	if err != nil {
		t.Fatal(err)
	}
	b, err := Identity(dir)
	if err != nil {
		t.Fatal(err)
	}
	if Fingerprint(a.Certificate[0]) != Fingerprint(b.Certificate[0]) {
		t.Fatal("identity changed between loads")
	}
	c, _ := Identity(t.TempDir())
	if Fingerprint(a.Certificate[0]) == Fingerprint(c.Certificate[0]) {
		t.Fatal("two directories share an identity")
	}
}

func TestStorePersists(t *testing.T) {
	path := filepath.Join(t.TempDir(), "sub", "s.json")
	s, err := OpenStore(path)
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := s.Get("k"); ok {
		t.Fatal("empty store has a key")
	}
	if err := s.Set("k", "v"); err != nil {
		t.Fatal(err)
	}
	s2, err := OpenStore(path)
	if err != nil {
		t.Fatal(err)
	}
	if v, ok := s2.Get("k"); !ok || v != "v" || s2.Len() != 1 {
		t.Fatalf("reloaded store = %q %v len %d", v, ok, s2.Len())
	}
}

func TestNormalizeFingerprint(t *testing.T) {
	const want = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff"
	for _, input := range []string{
		want,
		"00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
		"00:11:22:33:44:55:66:77:88:99:aa:bb:cc:dd:ee:ff:00:11:22:33:44:55:66:77:88:99:aa:bb:cc:dd:ee:ff",
		"  0011223344556677 8899aabbccddeeff 0011223344556677 8899aabbccddeeff  ",
	} {
		got, err := NormalizeFingerprint(input)
		if err != nil || got != want {
			t.Errorf("NormalizeFingerprint(%q) = %q, %v; want %q", input, got, err, want)
		}
	}
	for _, input := range []string{"", "abc", strings.Repeat("z", 64), want + "00", strings.Repeat("0", 63) + "-"} {
		if _, err := NormalizeFingerprint(input); err == nil {
			t.Errorf("NormalizeFingerprint(%q) accepted invalid input", input)
		}
	}
}
