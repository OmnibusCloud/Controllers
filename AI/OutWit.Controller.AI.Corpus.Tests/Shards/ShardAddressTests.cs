using System.Net;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>Which addresses count as the public internet: nothing of a machine's own surroundings does, in either address family or hidden inside an IPv6 form.</summary>
[TestFixture]
public sealed class ShardAddressTests
{
    #region Tests

    [TestCase("8.8.8.8")]
    [TestCase("1.1.1.1")]
    [TestCase("13.32.0.1")]
    [TestCase("100.63.255.255")]
    [TestCase("100.128.0.1")]
    [TestCase("172.15.255.255")]
    [TestCase("172.32.0.1")]
    [TestCase("192.167.1.1")]
    [TestCase("198.17.0.1")]
    [TestCase("223.255.255.254")]
    [TestCase("2600:9000:2000::1")]
    [TestCase("2a00:1450:4001::64")]
    [TestCase("::ffff:8.8.8.8")]
    [TestCase("2002:0808:0808::1")]
    [TestCase("64:ff9b::808:808")]
    public void PublicAddressesPassTest(string address)
    {
        Assert.That(ShardAddress.IsPublic(IPAddress.Parse(address)), Is.True);
    }

    [TestCase("127.0.0.1")]
    [TestCase("127.255.255.254")]
    [TestCase("0.0.0.0")]
    [TestCase("10.0.0.1")]
    [TestCase("10.255.255.255")]
    [TestCase("100.64.0.1")]
    [TestCase("100.127.255.255")]
    [TestCase("169.254.169.254")]
    [TestCase("172.16.0.1")]
    [TestCase("172.31.255.255")]
    [TestCase("192.168.1.1")]
    [TestCase("192.0.0.1")]
    [TestCase("192.0.2.10")]
    [TestCase("198.18.0.1")]
    [TestCase("198.19.255.255")]
    [TestCase("198.51.100.7")]
    [TestCase("203.0.113.7")]
    [TestCase("224.0.0.1")]
    [TestCase("239.255.255.250")]
    [TestCase("240.0.0.1")]
    [TestCase("255.255.255.255")]
    [TestCase("::")]
    [TestCase("::1")]
    [TestCase("::127.0.0.1")]
    [TestCase("::ffff:127.0.0.1")]
    [TestCase("::ffff:10.1.2.3")]
    [TestCase("::ffff:169.254.169.254")]
    [TestCase("fc00::1")]
    [TestCase("fd12:3456:789a::1")]
    [TestCase("fe80::1")]
    [TestCase("fec0::1")]
    [TestCase("ff02::1")]
    [TestCase("2001:db8::1")]
    [TestCase("2001:0:4136:e378:8000:63bf:3fff:fdd2")]
    [TestCase("2002:c0a8:0101::1")]
    [TestCase("2002:7f00:0001::1")]
    [TestCase("64:ff9b::c0a8:101")]
    [TestCase("64:ff9b::a9fe:a9fe")]
    public void AddressesOfAMachinesOwnSurroundingsDoNotTest(string address)
    {
        Assert.That(ShardAddress.IsPublic(IPAddress.Parse(address)), Is.False);
    }

    #endregion
}
