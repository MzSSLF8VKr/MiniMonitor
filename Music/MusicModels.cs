namespace MiniMonitor.Music;

/// <summary>音乐库视图。</summary>
public enum MusicView
{
    Songs,
    Albums,
    Artists,
    Genres,
    Folders,
    Liked,
    Perfect,
    Playlist,
    Recent,
    MostPlayed,
    NeverPlayed,
    Duplicates,
    Missing,
    Stats,
    Plugins,
    Settings
}

/// <summary>曲目。元数据来自本地标签，播放统计保存在本地 SQLite。</summary>
public sealed class MusicTrack
{
    public long Id { get; set; }
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Genre { get; set; } = "";
    public int Year { get; set; }
    public int TrackNo { get; set; }
    public int DiscNo { get; set; }
    public double DurationSeconds { get; set; }
    public int Bitrate { get; set; }
    public int SampleRate { get; set; }
    public int BitsPerSample { get; set; }
    public int Channels { get; set; }
    public string Codec { get; set; } = "";
    public long FileSize { get; set; }
    public string CoverKey { get; set; } = "";
    public bool Liked { get; set; }
    public int Rating { get; set; }
    public int PlayCount { get; set; }
    public long LastPlayed { get; set; }
    public long Added { get; set; }
    public long Modified { get; set; }
    public bool IsPerfect { get; set; }
    public bool Missing { get; set; }
    public double GainDb { get; set; }
    public string Comment { get; set; } = "";

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? System.IO.Path.GetFileNameWithoutExtension(Path)
        : Title.Trim();

    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist)
        ? (string.IsNullOrWhiteSpace(AlbumArtist) ? "未知艺术家" : AlbumArtist)
        : Artist;

    public string DisplayAlbum => string.IsNullOrWhiteSpace(Album) ? "未知专辑" : Album;

    public string DisplayGenre => string.IsNullOrWhiteSpace(Genre) ? "未知流派" : Genre;

    public TimeSpan Duration => TimeSpan.FromSeconds(Math.Max(0, DurationSeconds));

    public string DurationText => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours}:{Duration.Minutes:D2}:{Duration.Seconds:D2}"
        : $"{Duration.Minutes}:{Duration.Seconds:D2}";

    public bool IsHiRes => BitsPerSample >= 24 || SampleRate >= 88200;

    public string QualityText
    {
        get
        {
            var parts = new List<string>();
            if (SampleRate > 0)
            {
                parts.Add(SampleRate % 1000 == 0
                    ? $"{SampleRate / 1000.0:0.#} kHz"
                    : $"{SampleRate} Hz");
            }
            if (BitsPerSample > 0)
            {
                parts.Add($"{BitsPerSample} bit");
            }
            if (Bitrate > 0)
            {
                parts.Add($"{Bitrate} kbps");
            }
            return string.Join(" · ", parts);
        }
    }

    public MusicTrack Clone() => (MusicTrack)MemberwiseClone();
}

public sealed class MusicAlbum
{
    public string Name { get; set; } = "";
    public string Artist { get; set; } = "";
    public int Year { get; set; }
    public int TrackCount { get; set; }
    public string CoverKey { get; set; } = "";
    public double DurationSeconds { get; set; }
    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "未知艺术家" : Artist;
}

public sealed class MusicArtist
{
    public string Name { get; set; } = "";
    public int TrackCount { get; set; }
    public int AlbumCount { get; set; }
    public string CoverKey { get; set; } = "";
}

public sealed class MusicGenre
{
    public string Name { get; set; } = "";
    public int TrackCount { get; set; }
}

public sealed class MusicFolder
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public int TrackCount { get; set; }
    public bool IsPerfect { get; set; }
}

public sealed class MusicPlaylist
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int TrackCount { get; set; }
    public long Created { get; set; }
    public long Updated { get; set; }
}

/// <summary>曲目查询条件。</summary>
public sealed class MusicQuery
{
    public string? Search { get; set; }
    public string? Album { get; set; }
    public string? Artist { get; set; }
    public string? Genre { get; set; }
    public string? Folder { get; set; }
    public bool LikedOnly { get; set; }
    public bool PerfectOnly { get; set; }
    public long? PlaylistId { get; set; }
    public bool RecentOnly { get; set; }
    public bool NeverPlayed { get; set; }
    public bool HasPlayed { get; set; }
    public bool MissingOnly { get; set; }

    /// <summary>title / artist / album / duration / added / played / track</summary>
    public string Sort { get; set; } = "title";

    public bool Descending { get; set; }
    public int Limit { get; set; }
}

public sealed class MusicScanProgress
{
    public string Stage { get; init; } = "";
    public int Done { get; init; }
    public int Total { get; init; }
    public string CurrentFile { get; init; } = "";
}

public sealed class MusicScanResult
{
    public int Added { get; init; }
    public int Updated { get; init; }
    public int Skipped { get; init; }
    public int Removed { get; init; }
    public int Failed { get; init; }
    public int TotalFiles { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string LibraryPath { get; init; } = "";
    public bool PerfectMusicFound { get; init; }
}

public sealed class MusicPlayStat
{
    public string Name { get; set; } = "";
    public int Plays { get; set; }
    public double Seconds { get; set; }
}

public sealed class MusicStatistics
{
    public int TrackCount { get; set; }
    public int AlbumCount { get; set; }
    public int ArtistCount { get; set; }
    public double TotalSeconds { get; set; }
    public int TotalPlays { get; set; }
    public double PlayedSeconds { get; set; }
    public List<MusicPlayStat> TopTracks { get; set; } = new();
    public List<MusicPlayStat> TopArtists { get; set; } = new();
    public List<MusicPlayStat> TopAlbums { get; set; } = new();
}

/// <summary>同名重复音乐组：按文件名（不含扩展名）判定。</summary>
public sealed class DuplicateFileGroup
{
    public string Name { get; set; } = "";
    public List<MusicTrack> Tracks { get; set; } = new();
    public MusicTrack Keeper { get; set; } = null!;

    public bool HasProtectedTrack => Tracks.Any(t => t.Liked || t.IsPerfect);

    public int ExtraCount => Math.Max(0, Tracks.Count - 1);

    public string DisplaySummary => $"{Name} · {Tracks.Count} 份"
        + (HasProtectedTrack ? " · 保留到 PerfectMusic" : " · 保留一份");
}

public sealed class DuplicateCleanupResult
{
    public int Groups { get; set; }
    public int Moved { get; set; }
    public int Deleted { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}
