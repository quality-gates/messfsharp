namespace MessFSharp.Tests

open System
open System.Text.Json
open System.Xml.Linq
open Xunit
open MessFSharp
open MessFSharp.Domain

module ReporterTests =
    let private report =
        let location =
            { File = "src/<sample>&.fs"
              StartLine = 3
              StartColumn = 5
              EndLine = 4
              EndColumn = 9 }

        { ToolName = "messfsharp"
          Version = "9.8.7"
          Violations =
            [ { Location = location
                RuleName = "ExampleRule"
                RulesetName = "example"
                Priority = 2
                Description = "Use <safe> & deterministic output."
                Context =
                  { Namespace = Some "Example.Namespace"
                    Module = Some "SampleModule"
                    Type = Some "SampleType"
                    Member = Some "SampleMember" }
                HelpUri = Some "https://example.invalid/help" } ]
          Errors =
            [ { File = Some location.File
                Location =
                  Some
                      { location with
                          StartLine = 8
                          EndLine = 8 }
                Message = "Parse <failed> & continued." } ] }

    [<Fact>]
    let ``JSON and GitLab reports are parseable and preserve the report model`` () =
        use json = JsonDocument.Parse(Reports.render Json false report)
        Assert.Equal("messfsharp", json.RootElement.GetProperty("tool").GetString())
        Assert.Equal("9.8.7", json.RootElement.GetProperty("version").GetString())
        Assert.Equal(5, (json.RootElement.GetProperty("violations")[0]).GetProperty("startColumn").GetInt32())

        Assert.Equal(
            8,
            (json.RootElement.GetProperty("errors")[0]).GetProperty("location").GetProperty("startLine").GetInt32()
        )

        use gitlab = JsonDocument.Parse(Reports.render GitLab false report)
        let finding = gitlab.RootElement[0]
        Assert.Equal("messfsharp", finding.GetProperty("tool").GetString())
        Assert.Equal("9.8.7", finding.GetProperty("version").GetString())
        Assert.Equal("example", finding.GetProperty("ruleset").GetString())

        Assert.Equal(
            5,
            finding
                .GetProperty("location")
                .GetProperty("positions")
                .GetProperty("begin")
                .GetProperty("column")
                .GetInt32()
        )

        let processing = gitlab.RootElement[1]
        Assert.Equal("messfsharp-processing", processing.GetProperty("check_name").GetString())

        Assert.Equal(
            8,
            processing
                .GetProperty("location")
                .GetProperty("positions")
                .GetProperty("begin")
                .GetProperty("line")
                .GetInt32()
        )

    [<Fact>]
    let ``XML and Checkstyle reports are parseable and preserve metadata`` () =
        let xml = XDocument.Parse(Reports.render Xml false report)
        Assert.Equal("messfsharp", xml.Root.Attribute(XName.Get "tool").Value)
        Assert.Equal("9.8.7", xml.Root.Attribute(XName.Get "version").Value)

        Assert.Equal(
            "5",
            xml.Root
                .Element(XName.Get "violations")
                .Element(XName.Get "violation")
                .Attribute(XName.Get "startColumn")
                .Value
        )

        let checkstyle = XDocument.Parse(Reports.render Checkstyle false report)
        Assert.Equal("messfsharp", checkstyle.Root.Attribute(XName.Get "tool").Value)
        Assert.Equal("9.8.7", checkstyle.Root.Attribute(XName.Get "toolVersion").Value)

        let errors = checkstyle.Descendants(XName.Get "error") |> Seq.toArray
        Assert.Equal("9", errors[0].Attribute(XName.Get "endColumn").Value)
        Assert.Equal("example", errors[0].Attribute(XName.Get "ruleset").Value)
        Assert.Equal("messfsharp.processing", errors[1].Attribute(XName.Get "source").Value)
        Assert.Equal("8", errors[1].Attribute(XName.Get "line").Value)

    [<Fact>]
    let ``SARIF is version 2.1.0 and preserves findings and processing locations`` () =
        use sarif = JsonDocument.Parse(Reports.render Sarif false report)
        Assert.Equal("2.1.0", sarif.RootElement.GetProperty("version").GetString())

        let run = sarif.RootElement.GetProperty("runs")[0]
        Assert.Equal("messfsharp", run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString())
        Assert.Equal("9.8.7", run.GetProperty("tool").GetProperty("driver").GetProperty("version").GetString())

        let finding = run.GetProperty("results")[0]
        Assert.Equal("example", finding.GetProperty("properties").GetProperty("ruleset").GetString())

        Assert.Equal(
            5,
            (finding.GetProperty("locations")[0])
                .GetProperty("physicalLocation")
                .GetProperty("region")
                .GetProperty("startColumn")
                .GetInt32()
        )

        let notification =
            ((run.GetProperty("invocations")[0]).GetProperty("toolExecutionNotifications")[0])

        Assert.Equal(
            8,
            (notification.GetProperty("locations")[0])
                .GetProperty("physicalLocation")
                .GetProperty("region")
                .GetProperty("startLine")
                .GetInt32()
        )

    [<Fact>]
    let ``human and annotation reporters escape content and retain visible findings`` () =
        let text = Reports.render Text false report
        Assert.Contains("src/<sample>&.fs:3:ExampleRule: Use <safe> & deterministic output.", text)

        let ansi = Reports.render Ansi false report
        Assert.Contains("\u001b[31m", ansi)
        Assert.Contains("\u001b[0m", ansi)

        let html = Reports.render Html false report
        Assert.StartsWith("<!doctype html>", html)
        Assert.Contains("Use &lt;safe&gt; &amp; deterministic output.", html)
        Assert.EndsWith($"</html>{Environment.NewLine}", html)

        let github = Reports.render GitHub false report
        Assert.Contains("::error file=src/<sample>&.fs,line=3,col=5,endLine=4,endColumn=9,title=ExampleRule::", github)
        Assert.Contains("::error file=src/<sample>&.fs,line=8,col=5,title=messfsharp::", github)
