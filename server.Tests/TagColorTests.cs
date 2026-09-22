using Glance.Server;

namespace Glance.Server.Tests;

public class TagColorTests
{
    [Fact]
    public async Task ColorsPersistThroughRenameAndRejectInvalidValues()
    {
        await using var app = TestAppFixture.Create();
        var tag = await app.People.CreateTagAsync("Team", default);
        Assert.True(await app.People.UpdateTagAsync(tag.Id, new(null, null, "#12AB34"), default));
        Assert.True(await app.People.UpdateTagAsync(tag.Id, new("Renamed", null), default));
        var loaded = Assert.Single((await app.People.GetAsync(true, default)).Tags);
        Assert.Equal("#12ab34", loaded.Color);
        Assert.Equal("#12ab34", await app.Meta.GetValueAsync("tag_color:" + tag.Id, default));
        await Assert.ThrowsAsync<ArgumentException>(() => app.People.UpdateTagAsync(tag.Id, new(null, null, "red;"), default));
        await app.People.DeleteTagAsync(tag.Id, default);
        Assert.Null(await app.Meta.GetValueAsync("tag_color:" + tag.Id, default));
    }
}
