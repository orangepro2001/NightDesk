package trust

import (
	"crypto/subtle"
	"sync"
	"time"
)

// Verdict is the host's decision about a connecting viewer.
type Verdict int

const (
	Allowed     Verdict = iota
	Untrusted           // not paired, no pairing open, no (valid) password offered
	BadPassword         // a password or pairing code was offered and it is wrong
	Locked              // too many wrong passwords recently; try again later
)

const (
	maxFailures = 5
	lockout     = time.Minute
)

// Policy decides who may control this PC.
//
// Verification is off until SetRequireAuth(true). Once it is on, in order:
//  1. a device whose certificate fingerprint was paired before is allowed;
//  2. while pairing is open, a viewer offering the pairing code (or any
//     password when the code is empty) is paired and allowed, once;
//  3. otherwise the configured password, if any, must match.
//
// Wrong secrets are counted; after five the host refuses further attempts for a
// minute so a short code cannot be guessed over the network.
type Policy struct {
	mu          sync.Mutex
	trusted     *Store
	requireAuth bool
	password    string
	pairCode    string
	pairUntil   time.Time
	fails       int
	lockedUntil time.Time
	now         func() time.Time
}

func NewPolicy(trusted *Store) *Policy { return &Policy{trusted: trusted, now: time.Now} }

// SetRequireAuth turns verification on or off.
func (p *Policy) SetRequireAuth(on bool) {
	p.mu.Lock()
	p.requireAuth = on
	p.mu.Unlock()
}

// RequireAuth reports whether verification is on.
func (p *Policy) RequireAuth() bool {
	p.mu.Lock()
	defer p.mu.Unlock()
	return p.requireAuth
}

func (p *Policy) SetPassword(pw string) {
	p.mu.Lock()
	p.password = pw
	p.mu.Unlock()
}

// StartPairing opens a passwordless pairing window. The next new device is
// paired and allowed.
func (p *Policy) StartPairing(d time.Duration) { p.OpenPairing("", d) }

// OpenPairing opens pairing for d. A non-empty code must be offered as the
// viewer's password; an empty code accepts any new device.
func (p *Policy) OpenPairing(code string, d time.Duration) {
	p.mu.Lock()
	p.pairCode = code
	p.pairUntil = p.now().Add(d)
	p.mu.Unlock()
}

// ClosePairing ends the pairing window immediately.
func (p *Policy) ClosePairing() {
	p.mu.Lock()
	p.pairCode = ""
	p.pairUntil = time.Time{}
	p.mu.Unlock()
}

// PairingLeft is how long pairing stays open; zero if closed.
func (p *Policy) PairingLeft() time.Duration {
	p.mu.Lock()
	defer p.mu.Unlock()
	return max(p.pairUntil.Sub(p.now()), 0)
}

func (p *Policy) TrustedCount() int { return p.trusted.Len() }

// Check decides about a viewer with certificate fingerprint fp from addr, which
// offered password (possibly empty).
func (p *Policy) Check(fp, addr, password string) (Verdict, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	if !p.requireAuth {
		return Allowed, nil
	}
	if _, ok := p.trusted.Get(fp); ok {
		return Allowed, nil
	}
	now := p.now()
	if now.Before(p.lockedUntil) {
		return Locked, nil
	}
	pairingOpen := now.Before(p.pairUntil)
	if pairingOpen && p.pairCode == "" {
		// Passwordless pairing explicitly accepts the next new device.
		p.pairUntil = time.Time{}
		return Allowed, p.trusted.Set(fp, addr+" "+now.Format("2006-01-02 15:04"))
	}
	if pairingOpen && subtle.ConstantTimeCompare([]byte(password), []byte(p.pairCode)) == 1 {
		p.pairCode = ""
		p.pairUntil = time.Time{} // one device per pairing
		p.fails = 0
		// Failing to save only costs the user another pairing next time.
		return Allowed, p.trusted.Set(fp, addr+" "+now.Format("2006-01-02 15:04"))
	}
	if p.password != "" && subtle.ConstantTimeCompare([]byte(password), []byte(p.password)) == 1 {
		p.fails = 0
		return Allowed, nil
	}
	if password == "" {
		return Untrusted, nil
	}
	// Any non-empty wrong secret counts, including a bad pairing code while
	// the pairing window is open. Otherwise a six-digit code would be an
	// unlimited online guessing oracle on hosts without a permanent password.
	p.fails++
	if p.fails >= maxFailures {
		p.fails = 0
		p.lockedUntil = now.Add(lockout)
	}
	return BadPassword, nil
}
