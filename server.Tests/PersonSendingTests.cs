using System.Text.Json;
using System.Text.Json.Nodes;
namespace Glance.Server.Tests;
public sealed class PersonSendingTests
{
    [Fact]
    public async Task GroupSendPreservesOrderAndRollsBackWhenASourceIsMissing()
    {
        await using var app = TestAppFixture.Create();
        var from = await app.People.CreatePersonAsync("From", default);
        var to = await app.People.CreatePersonAsync("To", default);
        var first = await app.Tasks.CreateTaskAsync(new(TaskPages.PeopleMain, TestAppFixture.CreateTitle("First"),
            TestAppFixture.CreateContent("Private content"), 1, null, null, from.Id, IsPrivate: true), default);
        var second = await app.Tasks.CreateTaskAsync(new(TaskPages.PeopleMain, TestAppFixture.CreateTitle("Second"),
            TestAppFixture.CreateContent("Content"), 2, null, null, from.Id), default);
        var request = new TaskSendToPeopleRequest(new[] { from.Id, to.Id }, null, true);
        Assert.Null(await app.Tasks.SendToPeopleAsync(new[] { first.TaskId, "missing" }, request, default));
        Assert.Empty(await app.Tasks.GetPersonTasksAsync(to.Id, 0, default));
        Assert.Empty(await app.Tasks.GetSendEventsAsync(first.TaskId, default));

        var response = await app.Tasks.SendToPeopleAsync(new[] { first.TaskId, second.TaskId }, request, default);
        Assert.Equal(4, response!.Created);
        var received = await app.Tasks.GetPersonTasksAsync(to.Id, 0, default);
        Assert.Equal(new[] { response.TaskIds[0], response.TaskIds[2] }, received.Select(task => task.Id));
        Assert.True(received[0].IsPrivate);
        Assert.False(received[1].IsPrivate);
        Assert.Equal(2, (await app.Tasks.GetPersonTasksAsync(from.Id, 0, default)).Count);
        Assert.Equal(2, (await app.Tasks.GetSendEventsAsync(first.TaskId, default)).Count);
        Assert.Equal(2, (await app.Tasks.GetSendEventsAsync(second.TaskId, default)).Count);
    }

    [Fact]
    public async Task UnifiedSendIncludesDashboardAndPeopleAndPreservesPrivacy()
    {
        await using var app = TestAppFixture.Create();
        var alice = await app.People.CreatePersonAsync("Alice", default);
        var bob = await app.People.CreatePersonAsync("Bob", default);
        var created = await app.Tasks.CreateTaskAsync(new(TaskPages.PeopleMain,
            TestAppFixture.CreateTitle("Public title"), TestAppFixture.CreateContent("Private body"),
            1, null, null, alice.Id, IsPrivate: true), default);
        var result = await app.Tasks.SendToPeopleAsync(created.TaskId, new(new[] { bob.Id, bob.Id, alice.Id }, Array.Empty<string>(), true), default);
        Assert.Equal(2, result!.Created);
        var dashboard = Assert.Single(await app.Tasks.GetTasksByPageAsync(TaskPages.DashboardNew, 0, default));
        var recipient = Assert.Single(await app.Tasks.GetPersonTasksAsync(bob.Id, 0, default));
        foreach (var copy in new[] { dashboard, recipient })
        {
            Assert.True(copy.IsPrivate);
            Assert.Equal("From Alice", copy.OriginLabel);
            Assert.Contains("Private body", copy.Content.GetRawText());
        }
        Assert.Equal(2, (await app.Tasks.GetSendEventsAsync(created.TaskId, default)).Count);
        var dashboardOnly = await app.Tasks.SendToPeopleAsync(created.TaskId, new(Array.Empty<string>(), Array.Empty<string>(), true), default);
        Assert.Equal(1, dashboardOnly!.Created);
        // A dashboard source cannot send a redundant dashboard copy.
        Assert.Equal(0, (await app.Tasks.SendToPeopleAsync(dashboard.Id, new(Array.Empty<string>(), Array.Empty<string>(), true), default))!.Created);
    }

    [Fact]
    public async Task PeopleCopiesPreserveSourceAndRichContentAndAuditEachDestination()
    {
        await using var app=TestAppFixture.Create();
        var alice=await app.People.CreatePersonAsync("Alice",default);
        var bob=await app.People.CreatePersonAsync("Bob",default);
        var carol=await app.People.CreatePersonAsync("Carol",default);
        var archived=await app.People.CreatePersonAsync("Archived",default);
        var tag=await app.People.CreateTagAsync("Team",default);
        foreach(var person in new[]{alice,bob,carol,archived})await app.People.SetPersonTagsAsync(person.Id,new[]{tag.Id},default);
        await app.People.UpdatePersonAsync(archived.Id,new(null,null,true),default);
        using var rich=JsonDocument.Parse("""{"type":"doc","content":[{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph"}]},{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Child KEEP","marks":[{"type":"bold"}]}]},{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"image","attrs":{"src":"/fixture.png"}}]}]}]}]}]}]}""");
        var created=await app.Tasks.CreateTaskAsync(new(TaskPages.PeopleMain,TestAppFixture.CreateTitle("Follow up"),rich.RootElement.Clone(),1,null,null,alice.Id),default);
        var source=Assert.Single(await app.Tasks.GetPersonTasksAsync(alice.Id,0,default));
        Assert.Equal(created.UpdatedAt,source.CreatedAt);
        var response=await app.Tasks.SendToPeopleAsync(source.Id,new(new[]{alice.Id,bob.Id,archived.Id},new[]{tag.Id}),default);
        Assert.Equal(2,response!.Created);
        foreach(var person in new[]{bob,carol})
        {
            var copy=Assert.Single(await app.Tasks.GetPersonTasksAsync(person.Id,0,default));
            Assert.Equal("From Alice",copy.OriginLabel);
            Assert.True(copy.CreatedAt>=source.CreatedAt);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source.Title.GetRawText()),JsonNode.Parse(copy.Title.GetRawText())));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source.Content.GetRawText()),JsonNode.Parse(copy.Content.GetRawText())));
        }
        var retained=Assert.Single(await app.Tasks.GetPersonTasksAsync(alice.Id,0,default));
        Assert.Equal(source.Id,retained.Id);Assert.True(retained.SendMarkerVisible);
        var events=await app.Tasks.GetSendEventsAsync(source.Id,default);
        Assert.Equal(2,events.Count);Assert.All(events,e=>Assert.Equal("people-to-people",e.Direction));
        Assert.Contains(events,e=>e.DestinationKind=="person"&&e.DestinationLabel=="Bob");
        Assert.Contains(events,e=>e.DestinationKind=="tag"&&e.DestinationLabel=="Team");
        await app.Tasks.DismissSendMarkerAsync(source.Id,default);
        Assert.False(Assert.Single(await app.Tasks.GetPersonTasksAsync(alice.Id,0,default)).SendMarkerVisible);
        var selfOnly=await app.Tasks.SendToPeopleAsync(source.Id,new(new[]{alice.Id},Array.Empty<string>()),default);
        Assert.Equal(0,selfOnly!.Created);
        Assert.Equal(2,(await app.Tasks.GetSendEventsAsync(source.Id,default)).Count);
    }
}
