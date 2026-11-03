package host

import (
	"crypto/rand"
	"encoding/hex"
)

func randomSuffix() string {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil { // The timestamp remains unique enough for diagnostics; caller will not panic.
		return "random-unavailable"
	}
	return hex.EncodeToString(b[:])
}
