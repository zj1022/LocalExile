using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

/// <summary>/stash_add's primitive: tabs are appended at the end, named uniquely, and the header is saved so a reload
/// of the account shows them. A map tab's hidden pages are children and must not count as tabs the player sees.</summary>
public class StashAddTabsTests
{
    static AccountStash Open(ItemCatalog catalog, string account)
    {
        Saves.Open(Path.Combine(Path.GetTempPath(), "LocalExile-stashadd-" + Guid.NewGuid().ToString("N") + ".db"));
        return AccountStash.For(account, catalog);
    }

    [Fact]
    public void Appended_tabs_are_unique_and_survive_a_reload()
    {
        var catalog = new ItemCatalog("data");
        string account = "StashAdd" + Guid.NewGuid().ToString("N");
        var stash = Open(catalog, account);
        int roots = stash.VisibleCount;
        stash.EnsureMapChildren();   // 191 hidden pages of the starter map tab
        int start = stash.Count;
        int map = Enumerable.Range(0, roots).First(i => stash.TabType(i) == StashRules.Map);
        Assert.Equal(191, stash.UniqueChildren(map).Count);
        int collection = Enumerable.Range(0, roots).First(i => stash.TabType(i) == StashRules.Unique);
        Assert.Equal(start - roots - 191, stash.UniqueChildren(collection).Count);
        Assert.Equal(roots, stash.VisibleCount);

        var first = stash.AddTabs(7, 2, "Quad", 0x8a, 0x2b, 0xe2);
        var second = stash.AddTabs(7, 1, "Quad", 0x8a, 0x2b, 0xe2);
        Assert.Equal(new[] { start, start + 1 }, first);
        Assert.Equal(new[] { start + 2 }, second);
        Assert.Equal(roots + 3, stash.VisibleCount);
        Assert.All(first.Concat(second), i => Assert.Equal((byte)7, stash.TabType(i)));
        var names = Enumerable.Range(start, 3).Select(stash.NameOf).ToArray();
        Assert.Equal(3, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, n => Assert.StartsWith("Quad ", n));

        // Persist wrote the header: a reload of the same account shows the same tabs and names.
        AccountStash.Forget(account);
        var reloaded = AccountStash.For(account, catalog);
        Assert.Equal(roots + 3, reloaded.VisibleCount);
        Assert.Equal(names, Enumerable.Range(start, 3).Select(reloaded.NameOf).ToArray());

        Assert.Throws<ArgumentOutOfRangeException>(() => reloaded.AddTabs(99, 1, "Quad", 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => reloaded.AddTabs(0, 0, "Quad", 0, 0, 0));
    }
}
