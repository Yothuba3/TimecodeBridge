using System.Text.Json;
using Avalonia.Headless.XUnit;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Host.Bridge;
using Xunit;

namespace TimecodeBridge.Host.Tests;

public class CueEditingCommandTests
{
    private const string Draft = """
        {"name":"本編 IN","memo":"","triggerTime":"01:23:40:00","frameRate":"30","oscAddress":"/qlab/go",
         "additionalOscAddresses":["/light/1"],"arguments":[{"type":"int32","value":1},{"type":"string","value":"go"}],
         "targetHostIds":["h1"],"enabled":true,"sendTriggerTimeAsSeconds":false,"sendTimecode":null,
         "triggerOffset":"-00:00:00:05","autoMuteOnFire":true,"autoUnmuteAfter":"00:00:10:00"}
        """;

    private static string Data(ResultMessage r, string prop)
    {
        var v = JsonSerializer.SerializeToElement(r.Data, Protocol.Json).GetProperty(prop);
        return v.ValueKind is JsonValueKind.True or JsonValueKind.False ? (v.GetBoolean() ? "true" : "false") : v.ToString();
    }

    [AvaloniaFact]
    public void AddCreatesCueWithAllFieldsAndMarksDirty()
    {
        var h = new HostHarness();
        var r = h.Run("cue.add", $$$"""{"cue":{{{Draft}}}}""");
        Assert.True(r.Ok, r.Error?.Message);
        var cue = Assert.Single(h.Cues.Cues);
        Assert.Equal(Data(r, "id"), cue.Id);
        Assert.Equal("01:23:40:00", cue.TriggerTime.ToString());
        Assert.Equal("01:23:39:25", cue.GetEffectiveTriggerTime().ToString());
        Assert.Equal(2, cue.Arguments.Count);
        Assert.IsType<OscInt32Argument>(cue.Arguments[0]);
        Assert.True(cue.AutoMuteOnFire);
        Assert.Equal("00:00:10:00", cue.AutoUnmuteAfter!.Value.ToString());
        Assert.True(h.Project.HasUnsavedChanges);
        Assert.True(h.Projects.CanUndo);
    }

    [AvaloniaFact]
    public void AddRejectsInvalidDraftsWithFieldErrors()
    {
        var h = new HostHarness();
        Assert.Equal("cue.name", h.Run("cue.add", """{"cue":{"name":" ","triggerTime":"00:00:01:00","oscAddress":"/a"}}""").Error!.FieldErrors!.Keys.Single());
        Assert.Equal("cue.triggerTime", h.Run("cue.add", """{"cue":{"name":"x","triggerTime":"00:00:01:30","oscAddress":"/a"}}""").Error!.FieldErrors!.Keys.Single());
        Assert.Equal("cue.oscAddress", h.Run("cue.add", """{"cue":{"name":"x","triggerTime":"00:00:01:00","oscAddress":"go"}}""").Error!.FieldErrors!.Keys.Single());
        Assert.Equal("cue.triggerOffset", h.Run("cue.add", """{"cue":{"name":"x","triggerTime":"00:00:01:00","oscAddress":"/a","triggerOffset":"-00:00:02:00"}}""").Error!.FieldErrors!.Keys.Single());
        Assert.Empty(h.Cues.Cues);
        Assert.False(h.Project.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public void UpdateReplacesCueKeepingId()
    {
        var h = new HostHarness();
        var id = Data(h.Run("cue.add", $$$"""{"cue":{{{Draft}}}}"""), "id");
        var r = h.Run("cue.update", $$$"""{"id":"{{{id}}}","cue":{"name":"改名","triggerTime":"02:00:00:00","oscAddress":"/b"}}""");
        Assert.True(r.Ok, r.Error?.Message);
        var cue = Assert.Single(h.Cues.Cues);
        Assert.Equal(id, cue.Id);
        Assert.Equal("改名", cue.Name);
        Assert.Equal("/b", cue.OscAddress);
        Assert.Empty(cue.Arguments);
        Assert.Equal(ErrorCode.NotFound, h.Run("cue.update", """{"id":"nope","cue":{"name":"x","triggerTime":"00:00:01:00","oscAddress":"/a"}}""").Error!.Code);
    }

    [AvaloniaFact]
    public void DuplicateSingleAndSeries()
    {
        var h = new HostHarness();
        var id = Data(h.Run("cue.add", $$$"""{"cue":{{{Draft}}}}"""), "id");
        var single = h.Run("cue.duplicate", $$"""{"id":"{{id}}"}""");
        Assert.True(single.Ok);
        Assert.Equal(2, h.Cues.Cues.Count);
        Assert.Equal("本編 IN (コピー)", h.Cues.Cues[1].Name);
        Assert.Equal(h.Cues.Cues[0].TriggerTime, h.Cues.Cues[1].TriggerTime);

        var series = h.Run("cue.duplicate", $$"""{"id":"{{id}}","count":3,"intervalFrames":30}""");
        Assert.True(series.Ok);
        Assert.Equal(5, h.Cues.Cues.Count);
        Assert.Equal("01:23:41:00", h.Cues.Cues[2].TriggerTime.ToString());
        Assert.Equal("01:23:43:00", h.Cues.Cues[4].TriggerTime.ToString());
        Assert.Equal(ErrorCode.Validation, h.Run("cue.duplicate", $$"""{"id":"{{id}}","count":0}""").Error!.Code);

        var timed = h.Run("cue.duplicate", $$"""{"id":"{{id}}","count":2,"interval":"00:00:02:00","intervalFrames":1}""");
        Assert.True(timed.Ok, timed.Error?.Message);
        Assert.Equal(7, h.Cues.Cues.Count);
        Assert.Equal("01:23:42:00", h.Cues.Cues[5].TriggerTime.ToString());
        Assert.Equal("01:23:44:00", h.Cues.Cues[6].TriggerTime.ToString());
        var bad = h.Run("cue.duplicate", $$"""{"id":"{{id}}","count":2,"interval":"2s"}""");
        Assert.Equal(ErrorCode.Validation, bad.Error!.Code);
        Assert.Equal("interval", bad.Error!.FieldErrors!.Keys.Single());

        var dayEnd = Data(h.Run("cue.add", """{"cue":{"name":"終端","triggerTime":"23:59:59:29","frameRate":"30","oscAddress":"/end"}}"""), "id");
        var overflow = h.Run("cue.duplicate", $$"""{"id":"{{dayEnd}}","count":2,"interval":"00:00:00:01"}""");
        Assert.Equal(ErrorCode.Validation, overflow.Error!.Code);
        Assert.Equal("interval", overflow.Error!.FieldErrors!.Keys.Single());
        Assert.DoesNotContain(h.Cues.Cues, cue => cue.TriggerTime.Hours >= 24);
    }

    [AvaloniaFact]
    public void BatchUpdateAppliesOnlyPresentFieldsAndSkipsOutOfRangeOffsets()
    {
        var h = new HostHarness();
        var a = Data(h.Run("cue.add", """{"cue":{"name":"A","triggerTime":"00:00:00:10","oscAddress":"/a","memo":"keep"}}"""), "id");
        var b = Data(h.Run("cue.add", """{"cue":{"name":"B","triggerTime":"10:00:00:00","oscAddress":"/b","memo":"keep"}}"""), "id");

        var r = h.Run("cue.batchUpdate", $$$"""{"ids":["{{{a}}}","{{{b}}}","ghost"],"changes":{"oscAddress":"/all","enabled":false,"triggerOffset":"-00:00:01:00","sendTimecode":null}}""");
        Assert.True(r.Ok, r.Error?.Message);
        Assert.Equal("2", Data(r, "updated"));
        Assert.Equal("1", Data(r, "offsetSkipped"));

        var cueA = h.Cues.Cues.First(c => c.Id == a);
        var cueB = h.Cues.Cues.First(c => c.Id == b);
        Assert.Equal("/all", cueA.OscAddress);
        Assert.False(cueA.IsEnabled);
        Assert.Null(cueA.TriggerOffset);
        Assert.Equal("keep", cueA.Memo);
        Assert.NotNull(cueB.TriggerOffset);
        Assert.Equal("09:59:59:00", cueB.GetEffectiveTriggerTime().ToString());

        Assert.Equal(ErrorCode.Validation, h.Run("cue.batchUpdate", $$$"""{"ids":["{{{a}}}"],"changes":{"oscAddress":"bad"}}""").Error!.Code);
    }

    [AvaloniaFact]
    public void UndoRedoRestoreCueList()
    {
        var h = new HostHarness();
        h.Run("cue.add", """{"cue":{"name":"A","triggerTime":"00:00:01:00","oscAddress":"/a"}}""");
        Thread.Sleep(600); // 連続変更の集約窓(500ms)を超えて別の履歴にする
        h.Run("cue.add", """{"cue":{"name":"B","triggerTime":"00:00:02:00","oscAddress":"/b"}}""");
        Assert.Equal(2, h.Cues.Cues.Count);

        Assert.True(h.Run("app.undo").Ok);
        Assert.Single(h.Cues.Cues);
        Assert.Equal("A", h.Cues.Cues[0].Name);
        Assert.True(h.Run("app.undo").Ok);
        Assert.Empty(h.Cues.Cues);
        Assert.Equal(ErrorCode.InvalidState, h.Run("app.undo").Error!.Code);

        Assert.True(h.Run("app.redo").Ok);
        Assert.True(h.Run("app.redo").Ok);
        Assert.Equal(2, h.Cues.Cues.Count);
        Assert.Equal(ErrorCode.InvalidState, h.Run("app.redo").Error!.Code);
    }

    [AvaloniaFact]
    public void SaveThenOpenRoundTripsProjectAndSourceSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcb3-test-{Guid.NewGuid():N}.json");
        try
        {
            var h = new HostHarness();
            h.Run("host.add", """{"host":{"name":"QLab","ipAddress":"10.0.0.5","port":53000}}""");
            var hostId = h.Hosts.Hosts[0].Id;
            h.Run("cue.add", $$$"""{"cue":{"name":"A","triggerTime":"00:00:01:00","oscAddress":"/a","targetHostIds":["{{{hostId}}}"]}}""");
            h.Run("receive.setOffset", """{"value":"+00:00:00:08"}""");
            h.Run("cueSync.configure", """{"oscAddress":"/sync","targetHostIds":["x"]}""");
            h.State.SelectedInputDeviceId = "in-1";
            h.Run("generator.configure", """{"startTime":"05:00:00:00","frameRate":"25"}""");
            h.Project.SaveProject(path, h.Projects.Capture(includeSource: true));
            Assert.False(h.Project.HasUnsavedChanges);

            var h2 = new HostHarness();
            var r = h2.Run("project.open", $$"""{"path":"{{path.Replace("\\", "/")}}"}""");
            Assert.True(r.Ok, r.Error?.Message);
            Assert.Equal("false", Data(r, "cancelled"));
            Assert.Single(h2.Cues.Cues);
            Assert.Equal("QLab", h2.Hosts.Hosts[0].Name);
            Assert.Equal("+00:00:00:08", h2.Engine.Offset.ToString());
            Assert.Equal("/sync", h2.State.CueSync.OscAddress);
            Assert.Equal("in-1", h2.State.SelectedInputDeviceId);
            Assert.Equal(FrameRate.Fps25, h2.State.Generator.FrameRate);
            Assert.Contains("StartLtc:in-1:False", h2.Engine.Calls); // 読込後に受信を再開する
            Assert.False(h2.Project.HasUnsavedChanges);
            Assert.False(h2.Projects.CanUndo);
            Assert.Equal(path, h2.Project.CurrentFilePath);

            Assert.Equal(ErrorCode.NotFound, h2.Run("project.open", """{"path":"/nonexistent/x.json"}""").Error!.Code);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void NewClearsEverythingWhenNothingUnsaved()
    {
        var h = new HostHarness();
        h.Run("cue.add", """{"cue":{"name":"A","triggerTime":"00:00:01:00","oscAddress":"/a"}}""");
        h.Run("receive.setOffset", """{"value":"+00:00:01:00"}""");
        // ダイアログの親が無い(テスト)ときは確認なしで破棄する
        var r = h.Run("project.new");
        Assert.True(r.Ok);
        Assert.Equal("false", Data(r, "cancelled"));
        Assert.Empty(h.Cues.Cues);
        Assert.Equal(0, h.Engine.Offset.TotalFrames());
        Assert.False(h.Project.HasUnsavedChanges);
        Assert.False(h.Projects.CanUndo);
    }
}
