namespace TechTeaStudio.HyperionMinecraftLauncher.Core.News;

/// <summary>One news / article item from the Mojang launcher news feed.</summary>
public sealed record NewsEntry
{
    /// <summary>Headline.</summary>
    public required string Title { get; init; }

    /// <summary>Category, e.g. <c>"Minecraft: Java Edition"</c>.</summary>
    public required string Category { get; init; }

    /// <summary>ISO-8601 date string (the feed gives YYYY-MM-DD).</summary>
    public required string Date { get; init; }

    /// <summary>Plain-text teaser (1-3 sentences).</summary>
    public required string Text { get; init; }

    /// <summary>Absolute URL of the article hero image (we resolve the feed's relative <c>/images/...</c> against launchercontent.mojang.com).</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Absolute URL of the full article on minecraft.net.</summary>
    public string? ReadMoreLink { get; init; }

    /// <summary>Stable feed id (used to dedupe / track read state).</summary>
    public string? Id { get; init; }
}
