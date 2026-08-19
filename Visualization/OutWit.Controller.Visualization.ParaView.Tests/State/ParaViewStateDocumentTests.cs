using System.Text;
using OutWit.Controller.Visualization.ParaView.State;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.State;

/// <summary>
/// The hardened state parser: structure extraction on a typical state, and the security fixtures of
/// docs 03 section 16.2 — XXE, entities, deep/oversized XML, custom proxy definitions.
/// </summary>
[TestFixture]
public sealed class ParaViewStateDocumentTests
{
    #region Structure

    [Test]
    public void ParsesProxiesCollectionsViewsAndTimelineTest()
    {
        var xml = ParaViewStateBuilder.Typical("data/field.vtu").WithTimesteps(0, 0.5, 1).Build();
        var document = ParaViewStateDocument.Parse(ToStream(xml));

        Assert.Multiple(() =>
        {
            Assert.That(document.Version, Is.EqualTo("6.1.1"));
            Assert.That(document.Proxies.Select(me => me.Key), Does.Contain("sources/XMLUnstructuredGridReader"));
            Assert.That(document.Proxies.Select(me => me.Key), Does.Contain("filters/Contour"));
            Assert.That(document.Proxies.Select(me => me.Key), Does.Contain("views/RenderView"));
            Assert.That(document.ViewNames, Is.EqualTo(new[] { "RenderView1" }));
            Assert.That(document.TimestepValues, Is.EqualTo(new[] { 0.0, 0.5, 1.0 }));
            Assert.That(document.HasCustomProxyDefinitions, Is.False);
        });

        var reader = document.ProxiesInGroup("sources").First(me => me.Type == "XMLUnstructuredGridReader");
        Assert.That(reader.FindProperty("FileName")!.Values, Is.EqualTo(new[] { "data/field.vtu" }));
    }

    [Test]
    public void FileSeriesPropertyKeepsElementIndexOrderTest()
    {
        var xml = new ParaViewStateBuilder().Build();
        // Hand-rolled property with shuffled element indices.
        xml = xml.Replace("</ServerManagerState>",
            "  <Proxy group=\"sources\" type=\"XMLUnstructuredGridReader\" id=\"7\">\n" +
            "    <Property name=\"FileNames\" id=\"7.FileNames\" number_of_elements=\"3\">\n" +
            "      <Element index=\"2\" value=\"data/c.vtu\"/>\n" +
            "      <Element index=\"0\" value=\"data/a.vtu\"/>\n" +
            "      <Element index=\"1\" value=\"data/b.vtu\"/>\n" +
            "    </Property>\n" +
            "  </Proxy>\n</ServerManagerState>");

        var document = ParaViewStateDocument.Parse(ToStream(xml));
        var property = document.Proxies.Single(me => me.Id == "7").FindProperty("FileNames")!;

        Assert.That(property.Values, Is.EqualTo(new[] { "data/a.vtu", "data/b.vtu", "data/c.vtu" }));
    }

    [Test]
    public void StateWithoutTimeKeeperHasNullTimelineTest()
    {
        var xml = ParaViewStateBuilder.Typical().WithoutTimeKeeper().Build();
        var document = ParaViewStateDocument.Parse(ToStream(xml));

        Assert.That(document.TimestepValues, Is.Null);
    }

    [Test]
    public void CustomProxyDefinitionsAreFlaggedTest()
    {
        var xml = ParaViewStateBuilder.Typical()
            .WithExtraStateContent("  <CustomProxyDefinitions>\n    <CustomProxyDefinition name=\"MyFilter\" group=\"filters\"><CompoundSourceProxy/></CustomProxyDefinition>\n  </CustomProxyDefinitions>\n")
            .Build();

        var document = ParaViewStateDocument.Parse(ToStream(xml));
        Assert.That(document.HasCustomProxyDefinitions, Is.True);
    }

    #endregion

    #region Security fixtures

    [Test]
    public void ExternalEntityAttemptIsRejectedTest()
    {
        var xml = ParaViewStateBuilder.Typical()
            .WithPrefix("<?xml version=\"1.0\"?>\n<!DOCTYPE ParaView [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>\n")
            .Build();

        var exception = Assert.Throws<ParaViewStateFormatException>(() => ParaViewStateDocument.Parse(ToStream(xml)));
        Assert.That(exception!.Message, Does.Contain("DTD").Or.Contain("entit").Or.Contain("well-formed"));
    }

    [Test]
    public void EntityExpansionBombIsRejectedTest()
    {
        var xml = "<?xml version=\"1.0\"?>\n<!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\">]>\n<ParaView><ServerManagerState version=\"6.1.1\"><Proxy group=\"x\" type=\"&lol2;\"/></ServerManagerState></ParaView>";

        Assert.Throws<ParaViewStateFormatException>(() => ParaViewStateDocument.Parse(ToStream(xml)));
    }

    [Test]
    public void DeeplyNestedXmlIsRejectedTest()
    {
        var depth = ParaViewInputLimits.MAX_XML_DEPTH + 5;
        var xml = "<ParaView><ServerManagerState version=\"6.1.1\">" + string.Concat(Enumerable.Repeat("<a>", depth)) + string.Concat(Enumerable.Repeat("</a>", depth)) + "</ServerManagerState></ParaView>";

        var exception = Assert.Throws<ParaViewStateFormatException>(() => ParaViewStateDocument.Parse(ToStream(xml)));
        Assert.That(exception!.Message, Does.Contain("deeper"));
    }

    [Test]
    public void OversizedAttributeIsRejectedTest()
    {
        var xml = "<ParaView><ServerManagerState version=\"6.1.1\"><Proxy group=\"sources\" type=\"X\" id=\"1\"><Property name=\"P\"><Element index=\"0\" value=\"" + new string('v', ParaViewInputLimits.MAX_XML_TEXT_CHARS + 1) + "\"/></Property></Proxy></ServerManagerState></ParaView>";

        var exception = Assert.Throws<ParaViewStateFormatException>(() => ParaViewStateDocument.Parse(ToStream(xml)));
        Assert.That(exception!.Message, Does.Contain("exceeds"));
    }

    [Test]
    public void NonStateRootIsRejectedTest()
    {
        Assert.Throws<ParaViewStateFormatException>(() => ParaViewStateDocument.Parse(ToStream("<html><body/></html>")));
    }

    [Test]
    public void MalformedXmlIsRejectedTest()
    {
        Assert.Throws<ParaViewStateFormatException>(() => ParaViewStateDocument.Parse(ToStream("<ParaView><ServerManagerState version=\"6.1.1\"><Proxy></ServerManagerState>")));
    }

    [Test]
    public void OversizedStateFileIsRejectedBeforeParsingTest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pv_state_{Guid.NewGuid():N}.pvsm");
        try
        {
            using (var stream = new FileStream(path, FileMode.Create))
                stream.SetLength(ParaViewInputLimits.MAX_STATE_BYTES + 1);

            var exception = Assert.Throws<ParaViewStateFormatException>(() => ParaViewStateDocument.Parse(path));
            Assert.That(exception!.Message, Does.Contain("limit"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion

    #region Tools

    private static MemoryStream ToStream(string xml)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }

    #endregion
}
