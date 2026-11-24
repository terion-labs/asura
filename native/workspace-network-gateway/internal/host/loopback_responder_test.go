package host

import (
	"bytes"
	"context"
	"errors"
	"net"
	"net/netip"
	"sync/atomic"
	"time"

	M "github.com/xjasonlyu/tun2socks/v2/metadata"
	"golang.org/x/net/icmp"
	"golang.org/x/net/ipv4"
	"golang.org/x/net/ipv6"
)

// loopbackResponderProxy stands in for the host's direct route in packet-path
// tests. The guest addresses a routable documentation address, which the
// userspace stack forwards where a loopback destination would be dropped, and
// the proxy carries each datagram to a responder on the host's loopback. A
// firewall that blocks incoming connections drops datagrams a host sends to
// its own LAN address, but it never filters loopback.
type loopbackResponderProxy struct {
	responder *net.UDPAddr
}

func (*loopbackResponderProxy) DialContext(context.Context, *M.Metadata) (net.Conn, error) {
	return nil, errors.ErrUnsupported
}

func (value *loopbackResponderProxy) DialUDP(*M.Metadata) (net.PacketConn, error) {
	connection, err := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if err != nil {
		return nil, err
	}
	return &redirectedPacketConn{UDPConn: connection, responder: value.responder}, nil
}

// redirectedPacketConn sends what the guest addressed to the responder and
// reports the responder's replies as coming from the guest's destination.
type redirectedPacketConn struct {
	*net.UDPConn
	responder   *net.UDPAddr
	destination atomic.Pointer[net.Addr]
}

func (connection *redirectedPacketConn) WriteTo(payload []byte, destination net.Addr) (int, error) {
	connection.destination.Store(&destination)
	return connection.UDPConn.WriteTo(payload, connection.responder)
}

func (connection *redirectedPacketConn) ReadFrom(buffer []byte) (int, net.Addr, error) {
	count, source, err := connection.UDPConn.ReadFrom(buffer)
	if destination := connection.destination.Load(); destination != nil {
		source = *destination
	}
	return count, source, err
}

func loopbackUDPResponder() (net.PacketConn, error) {
	return net.ListenPacket("udp4", "127.0.0.1:0")
}

// hostAnswersLoopbackEcho reports whether this host answers an echo request
// that an unprivileged ping socket sends to its own loopback. A firewall in
// stealth mode drops even those, and then any echo through the host can only
// time out.
func hostAnswersLoopbackEcho(family int, destination netip.Addr) bool {
	connection, err := openPingSocket(family)
	if err != nil {
		return false
	}
	defer connection.Close()
	var requestType, replyType icmp.Type = ipv4.ICMPTypeEcho, ipv4.ICMPTypeEchoReply
	protocolNumber := 1
	if family == 6 {
		requestType, replyType = ipv6.ICMPTypeEchoRequest, ipv6.ICMPTypeEchoReply
		protocolNumber = 58
	}
	payload := []byte("asura-loopback-probe")
	request, err := (&icmp.Message{
		Type: requestType,
		Body: &icmp.Echo{ID: 0x4754, Seq: 1, Data: payload},
	}).Marshal(nil)
	if err != nil {
		return false
	}
	_ = connection.SetDeadline(time.Now().Add(time.Second))
	if _, err := connection.WriteTo(request, &net.UDPAddr{IP: net.IP(destination.AsSlice())}); err != nil {
		return false
	}
	// A ping socket also receives other ICMP traffic, such as IPv6 neighbor
	// discovery, so only this probe's own echo reply counts as an answer.
	buffer := make([]byte, 1500)
	for {
		count, _, err := connection.ReadFrom(buffer)
		if err != nil {
			return false
		}
		message, err := icmp.ParseMessage(protocolNumber, buffer[:count])
		if err != nil || message.Type != replyType {
			continue
		}
		if echo, ok := message.Body.(*icmp.Echo); ok && bytes.Equal(echo.Data, payload) {
			return true
		}
	}
}
