using Microsoft.Data.Sqlite;
using System.Globalization;

namespace MiniMonitor.Music;

/// <summary>
/// 离线音乐播放器本地库（SQLite）。曲目、播放列表、播放历史与统计全部保存在
/// %APPDATA%\MiniMonitor\music.db，不上传任何数据。
/// </summary>
public sealed class MusicLibrary
{
    private const string TrackColumns = """
        id,path,title,artist,album_artist,album,genre,year,track_no,disc_no,
        duration,bitrate,sample_rate,bits_per_sample,channels,codec,file_size,
        cover_key,liked,rating,play_count,last_played,added,modified,
        is_perfect,missing,gain_db,comment
        """;

    private readonly object _gate = new();
    private readonly string _dbPath;

    public static string DefaultPath { get; } = Path.Combine(AppConfig.ConfigDir, "music.db");

    public MusicLibrary(string? path = null)
    {
        _dbPath = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        Initialize();
    }

    public string DatabasePath => _dbPath;

    private SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS music_tracks (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    path TEXT NOT NULL UNIQUE,
                    title TEXT NOT NULL DEFAULT '',
                    artist TEXT NOT NULL DEFAULT '',
                    album_artist TEXT NOT NULL DEFAULT '',
                    album TEXT NOT NULL DEFAULT '',
                    genre TEXT NOT NULL DEFAULT '',
                    year INTEGER NOT NULL DEFAULT 0,
                    track_no INTEGER NOT NULL DEFAULT 0,
                    disc_no INTEGER NOT NULL DEFAULT 0,
                    duration REAL NOT NULL DEFAULT 0,
                    bitrate INTEGER NOT NULL DEFAULT 0,
                    sample_rate INTEGER NOT NULL DEFAULT 0,
                    bits_per_sample INTEGER NOT NULL DEFAULT 0,
                    channels INTEGER NOT NULL DEFAULT 0,
                    codec TEXT NOT NULL DEFAULT '',
                    file_size INTEGER NOT NULL DEFAULT 0,
                    cover_key TEXT NOT NULL DEFAULT '',
                    liked INTEGER NOT NULL DEFAULT 0,
                    rating INTEGER NOT NULL DEFAULT 0,
                    play_count INTEGER NOT NULL DEFAULT 0,
                    last_played INTEGER NOT NULL DEFAULT 0,
                    added INTEGER NOT NULL DEFAULT 0,
                    modified INTEGER NOT NULL DEFAULT 0,
                    is_perfect INTEGER NOT NULL DEFAULT 0,
                    missing INTEGER NOT NULL DEFAULT 0,
                    gain_db REAL NOT NULL DEFAULT 0,
                    comment TEXT NOT NULL DEFAULT '',
                    seen INTEGER NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS idx_music_tracks_album ON music_tracks(album, artist);
                CREATE INDEX IF NOT EXISTS idx_music_tracks_artist ON music_tracks(artist);
                CREATE INDEX IF NOT EXISTS idx_music_tracks_added ON music_tracks(added DESC);
                CREATE INDEX IF NOT EXISTS idx_music_tracks_liked ON music_tracks(liked);
                CREATE TABLE IF NOT EXISTS music_playlists (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    created INTEGER NOT NULL DEFAULT 0,
                    updated INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS music_playlist_tracks (
                    playlist_id INTEGER NOT NULL,
                    track_id INTEGER NOT NULL,
                    position INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (playlist_id, track_id)
                );
                CREATE TABLE IF NOT EXISTS music_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    track_id INTEGER NOT NULL,
                    played_at INTEGER NOT NULL,
                    seconds REAL NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_music_history_time ON music_history(played_at DESC);
                CREATE TABLE IF NOT EXISTS music_meta (
                    key TEXT PRIMARY KEY,
                    value TEXT
                );
                PRAGMA user_version=1;
                """;
            command.ExecuteNonQuery();
        }
    }

    // ================= 扫描写入 =================

    public void BeginScan()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE music_tracks SET seen=0";
            command.ExecuteNonQuery();
        }
    }

    /// <summary>写入或更新曲目；返回 (id, 是否新增, 是否有变化)。</summary>
    public (long Id, bool Added, bool Changed) UpsertTrack(MusicTrack track, bool perfectAsLiked)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var lookup = connection.CreateCommand();
            lookup.CommandText = "SELECT id,modified FROM music_tracks WHERE path=$path";
            lookup.Parameters.AddWithValue("$path", track.Path);
            long existingId = 0;
            long existingModified = -1;
            using (var reader = lookup.ExecuteReader())
            {
                if (reader.Read())
                {
                    existingId = reader.GetInt64(0);
                    existingModified = reader.GetInt64(1);
                }
            }

            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO music_tracks
                (path,title,artist,album_artist,album,genre,year,track_no,disc_no,
                 duration,bitrate,sample_rate,bits_per_sample,channels,codec,file_size,cover_key,
                 liked,rating,play_count,last_played,added,modified,is_perfect,missing,gain_db,comment,seen)
                VALUES
                ($path,$title,$artist,$albumArtist,$album,$genre,$year,$trackNo,$discNo,
                 $duration,$bitrate,$sampleRate,$bits,$channels,$codec,$fileSize,$coverKey,
                 CASE WHEN $perfect=1 AND $autoLike=1 THEN 1 ELSE 0 END,0,0,0,$now,$modified,
                 $perfect,0,$gain,$comment,1)
                ON CONFLICT(path) DO UPDATE SET
                    title=excluded.title, artist=excluded.artist, album_artist=excluded.album_artist,
                    album=excluded.album, genre=excluded.genre, year=excluded.year,
                    track_no=excluded.track_no, disc_no=excluded.disc_no,
                    duration=excluded.duration, bitrate=excluded.bitrate,
                    sample_rate=excluded.sample_rate, bits_per_sample=excluded.bits_per_sample,
                    channels=excluded.channels, codec=excluded.codec, file_size=excluded.file_size,
                    cover_key=excluded.cover_key, modified=excluded.modified,
                    is_perfect=excluded.is_perfect, missing=0, seen=1,
                    gain_db=excluded.gain_db,
                    comment=excluded.comment,
                    liked=CASE WHEN excluded.is_perfect=1 AND $autoLike=1 THEN 1 ELSE music_tracks.liked END;
                """;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double duration = double.IsFinite(track.DurationSeconds)
                ? Math.Max(0, track.DurationSeconds) : 0;
            double gain = double.IsFinite(track.GainDb)
                ? Math.Clamp(track.GainDb, -60, 60) : 0;
            command.Parameters.AddWithValue("$path", track.Path);
            command.Parameters.AddWithValue("$title", track.Title ?? "");
            command.Parameters.AddWithValue("$artist", track.Artist ?? "");
            command.Parameters.AddWithValue("$albumArtist", track.AlbumArtist ?? "");
            command.Parameters.AddWithValue("$album", track.Album ?? "");
            command.Parameters.AddWithValue("$genre", track.Genre ?? "");
            command.Parameters.AddWithValue("$year", Math.Max(0, track.Year));
            command.Parameters.AddWithValue("$trackNo", Math.Max(0, track.TrackNo));
            command.Parameters.AddWithValue("$discNo", Math.Max(0, track.DiscNo));
            command.Parameters.AddWithValue("$duration", duration);
            command.Parameters.AddWithValue("$bitrate", track.Bitrate);
            command.Parameters.AddWithValue("$sampleRate", track.SampleRate);
            command.Parameters.AddWithValue("$bits", track.BitsPerSample);
            command.Parameters.AddWithValue("$channels", track.Channels);
            command.Parameters.AddWithValue("$codec", track.Codec ?? "");
            command.Parameters.AddWithValue("$fileSize", track.FileSize);
            command.Parameters.AddWithValue("$coverKey", track.CoverKey ?? "");
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$modified", track.Modified);
            command.Parameters.AddWithValue("$perfect", track.IsPerfect ? 1 : 0);
            command.Parameters.AddWithValue("$autoLike", perfectAsLiked ? 1 : 0);
            command.Parameters.AddWithValue("$gain", gain);
            command.Parameters.AddWithValue("$comment", track.Comment ?? "");
            command.ExecuteNonQuery();

            long id = existingId;
            if (id == 0)
            {
                using var idCommand = connection.CreateCommand();
                idCommand.CommandText = "SELECT id FROM music_tracks WHERE path=$path";
                idCommand.Parameters.AddWithValue("$path", track.Path);
                id = Convert.ToInt64(idCommand.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
            }
            return (id, existingId == 0, existingId != 0 && existingModified != track.Modified);
        }
    }

    /// <summary>结束扫描：seen=0 的曲目标记为缺失并返回数量。</summary>
    public int FinishScan()
    {
        lock (_gate)
        {
            using var connection = Open();
            using (var update = connection.CreateCommand())
            {
                update.CommandText = "UPDATE music_tracks SET missing=1 WHERE seen=0";
                update.ExecuteNonQuery();
            }
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT changes()";
            return Convert.ToInt32(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        }
    }

    // ================= 查询 =================

    public MusicTrack? GetTrack(long id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {TrackColumns} FROM music_tracks WHERE id=$id";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadTrack(reader) : null;
        }
    }

    public List<MusicTrack> GetAllTracks()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {TrackColumns} FROM music_tracks WHERE missing=0 ORDER BY title COLLATE NOCASE";
            using var reader = command.ExecuteReader();
            var list = new List<MusicTrack>();
            while (reader.Read())
            {
                list.Add(ReadTrack(reader));
            }
            return list;
        }
    }

    /// <summary>路径 → 文件修改时间（UTC Ticks），用于增量扫描。</summary>
    public Dictionary<string, long> GetTrackIndex()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT path,modified FROM music_tracks";
            using var reader = command.ExecuteReader();
            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                map[reader.GetString(0)] = reader.GetInt64(1);
            }
            return map;
        }
    }

    /// <summary>批量标记未变化的文件仍存在，使用单连接与事务，避免大库逐条打开连接。</summary>
    public void MarkSeenBatch(IReadOnlyCollection<string> paths, bool autoLikePerfect)
    {
        if (paths.Count == 0)
        {
            return;
        }
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE music_tracks
                SET seen=1, missing=0,
                    liked=CASE WHEN $auto=1 AND is_perfect=1 THEN 1 ELSE liked END
                WHERE path=$path
                """;
            var pathParameter = command.Parameters.Add("$path", SqliteType.Text);
            command.Parameters.AddWithValue("$auto", autoLikePerfect ? 1 : 0);
            foreach (string path in paths)
            {
                pathParameter.Value = path;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    /// <summary>扫描时标记未变化的文件仍存在；PerfectMusic 自动喜欢开启时补写喜欢状态。</summary>
    public void MarkSeen(string path, bool autoLikePerfect = false)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE music_tracks
                SET seen=1, missing=0,
                    liked=CASE WHEN $auto=1 AND is_perfect=1 THEN 1 ELSE liked END
                WHERE path=$path
                """;
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$auto", autoLikePerfect ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    public List<MusicTrack> QueryTracks(MusicQuery query)
    {
        var sql = new System.Text.StringBuilder();
        sql.Append($"SELECT {TrackColumns} FROM music_tracks t");
        if (query.PlaylistId.HasValue)
        {
            sql.Append(" JOIN music_playlist_tracks pt ON pt.track_id=t.id AND pt.playlist_id=$playlist");
        }
        sql.Append(" WHERE 1=1");
        if (!query.MissingOnly)
        {
            sql.Append(" AND t.missing=0");
        }
        else
        {
            sql.Append(" AND t.missing=1");
        }
        if (query.LikedOnly) sql.Append(" AND t.liked=1");
        if (query.PerfectOnly) sql.Append(" AND t.is_perfect=1");
        if (query.NeverPlayed) sql.Append(" AND t.play_count=0");
        if (query.HasPlayed) sql.Append(" AND t.play_count>0");
        if (!string.IsNullOrWhiteSpace(query.Album))
        {
            sql.Append(query.Album == "未知专辑"
                ? " AND t.album=''"
                : " AND t.album=$album COLLATE NOCASE");
        }
        if (!string.IsNullOrWhiteSpace(query.Artist))
        {
            sql.Append(query.Artist == "未知艺术家"
                ? " AND (t.artist='' AND t.album_artist='')"
                : " AND (t.artist=$artist COLLATE NOCASE OR t.album_artist=$artist COLLATE NOCASE)");
        }
        if (!string.IsNullOrWhiteSpace(query.Genre))
        {
            sql.Append(query.Genre == "未知流派"
                ? " AND t.genre=''"
                : " AND t.genre=$genre COLLATE NOCASE");
        }
        if (!string.IsNullOrWhiteSpace(query.Folder))
        {
            sql.Append(" AND t.path LIKE $folder ESCAPE '\\'");
        }
        string? search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql.Append("""
                 AND (t.title LIKE $search ESCAPE '\'
                   OR t.artist LIKE $search ESCAPE '\'
                   OR t.album_artist LIKE $search ESCAPE '\'
                   OR t.album LIKE $search ESCAPE '\'
                   OR t.genre LIKE $search ESCAPE '\'
                   OR t.path LIKE $search ESCAPE '\')
                """);
        }
        sql.Append(OrderBy(query));

        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql.ToString();
            if (query.PlaylistId.HasValue)
            {
                command.Parameters.AddWithValue("$playlist", query.PlaylistId.Value);
            }
            if (!string.IsNullOrWhiteSpace(query.Album) && query.Album != "未知专辑")
            {
                command.Parameters.AddWithValue("$album", query.Album);
            }
            if (!string.IsNullOrWhiteSpace(query.Artist) && query.Artist != "未知艺术家")
            {
                command.Parameters.AddWithValue("$artist", query.Artist);
            }
            if (!string.IsNullOrWhiteSpace(query.Genre) && query.Genre != "未知流派")
            {
                command.Parameters.AddWithValue("$genre", query.Genre);
            }
            if (!string.IsNullOrWhiteSpace(query.Folder))
            {
                string prefix = EscapeLike(query.Folder.TrimEnd('\\'));
                command.Parameters.AddWithValue("$folder", prefix + "\\\\%");
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                command.Parameters.AddWithValue("$search", "%" + EscapeLike(search) + "%");
            }
            using var reader = command.ExecuteReader();
            var list = new List<MusicTrack>();
            while (reader.Read())
            {
                list.Add(ReadTrack(reader));
                if (query.Limit > 0 && list.Count >= query.Limit)
                {
                    break;
                }
            }
            return list;
        }
    }

    private static string OrderBy(MusicQuery query)
    {
        string direction = query.Descending ? "DESC" : "ASC";
        string order = query.Sort switch
        {
            "artist" => $"t.artist COLLATE NOCASE {direction}, t.album COLLATE NOCASE, t.disc_no, t.track_no",
            "album" => $"t.album COLLATE NOCASE {direction}, t.disc_no, t.track_no, t.title COLLATE NOCASE",
            "duration" => $"t.duration {direction}, t.title COLLATE NOCASE",
            "added" => $"t.added {direction}, t.id {direction}",
            "played" => $"t.play_count {direction}, t.last_played {direction}",
            "track" => $"t.album COLLATE NOCASE, t.disc_no, t.track_no {direction}",
            _ => $"t.title COLLATE NOCASE {direction}, t.artist COLLATE NOCASE"
        };
        return " ORDER BY " + order;
    }

    public List<MusicAlbum> GetAlbums(string? search = null)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN album='' THEN '未知专辑' ELSE album END AS album_name,
                       CASE WHEN album_artist<>'' THEN album_artist WHEN artist<>'' THEN artist ELSE '未知艺术家' END AS album_artist,
                       MAX(year), COUNT(*), SUM(duration), MAX(cover_key)
                FROM music_tracks
                WHERE missing=0 AND ($search='' OR album LIKE $like ESCAPE '\' OR artist LIKE $like ESCAPE '\'
                       OR album_artist LIKE $like ESCAPE '\')
                GROUP BY album_name, album_artist
                ORDER BY album_name COLLATE NOCASE;
                """;
            string query = search?.Trim() ?? "";
            command.Parameters.AddWithValue("$search", query);
            command.Parameters.AddWithValue("$like", "%" + EscapeLike(query) + "%");
            using var reader = command.ExecuteReader();
            var list = new List<MusicAlbum>();
            while (reader.Read())
            {
                list.Add(new MusicAlbum
                {
                    Name = reader.GetString(0),
                    Artist = reader.GetString(1),
                    Year = reader.GetInt32(2),
                    TrackCount = reader.GetInt32(3),
                    DurationSeconds = reader.GetDouble(4),
                    CoverKey = reader.GetString(5)
                });
            }
            return list;
        }
    }

    public List<MusicArtist> GetArtists(string? search = null)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN artist<>'' THEN artist WHEN album_artist<>'' THEN album_artist
                            ELSE '未知艺术家' END AS artist_name,
                       COUNT(*), COUNT(DISTINCT album), MAX(cover_key)
                FROM music_tracks
                WHERE missing=0 AND ($search='' OR artist LIKE $like ESCAPE '\' OR album_artist LIKE $like ESCAPE '\')
                GROUP BY artist_name
                ORDER BY artist_name COLLATE NOCASE;
                """;
            string query = search?.Trim() ?? "";
            command.Parameters.AddWithValue("$search", query);
            command.Parameters.AddWithValue("$like", "%" + EscapeLike(query) + "%");
            using var reader = command.ExecuteReader();
            var list = new List<MusicArtist>();
            while (reader.Read())
            {
                list.Add(new MusicArtist
                {
                    Name = reader.GetString(0),
                    TrackCount = reader.GetInt32(1),
                    AlbumCount = reader.GetInt32(2),
                    CoverKey = reader.GetString(3)
                });
            }
            return list;
        }
    }

    public List<MusicGenre> GetGenres(string? search = null)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN genre='' THEN '未知流派' ELSE genre END AS genre_name, COUNT(*)
                FROM music_tracks WHERE missing=0 AND ($search='' OR genre LIKE $like ESCAPE '\')
                GROUP BY genre_name ORDER BY genre_name COLLATE NOCASE;
                """;
            string query = search?.Trim() ?? "";
            command.Parameters.AddWithValue("$search", query);
            command.Parameters.AddWithValue("$like", "%" + EscapeLike(query) + "%");
            using var reader = command.ExecuteReader();
            var list = new List<MusicGenre>();
            while (reader.Read())
            {
                list.Add(new MusicGenre { Name = reader.GetString(0), TrackCount = reader.GetInt32(1) });
            }
            return list;
        }
    }

    public List<MusicFolder> GetFolders()
    {
        var tracks = GetAllTracks();
        return tracks
            .GroupBy(t => t.Folder, StringComparer.OrdinalIgnoreCase)
            .Select(g => new MusicFolder
            {
                Path = g.Key,
                Name = Path.GetFileName(g.Key) is { Length: > 0 } name ? name : g.Key,
                TrackCount = g.Count(),
                IsPerfect = g.Any(t => t.IsPerfect)
            })
            .OrderByDescending(f => f.IsPerfect)
            .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public List<MusicTrack> GetDuplicates()
    {
        var tracks = GetAllTracks();
        var duplicateIds = tracks
            .GroupBy(t => (
                Title: t.DisplayTitle.ToLowerInvariant(),
                Artist: t.DisplayArtist.ToLowerInvariant(),
                Duration: Math.Round(t.DurationSeconds / 3.0)))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(t => t.Id))
            .ToHashSet();
        return tracks.Where(t => duplicateIds.Contains(t.Id))
            .OrderBy(t => t.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.DurationSeconds)
            .ToList();
    }

    public List<MusicTrack> GetMissingTracks()
    {
        var query = new MusicQuery { MissingOnly = true, Sort = "title" };
        return QueryTracks(query);
    }

    /// <summary>按文件名（不含扩展名）找出音乐目录内的同名重复曲目。</summary>
    public List<DuplicateFileGroup> GetDuplicateFileGroups(string libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            return new List<DuplicateFileGroup>();
        }
        var tracks = GetAllTracks()
            .Where(track => IsUnderLibrary(track.Path, libraryRoot))
            .ToList();
        return tracks
            .GroupBy(track => Path.GetFileNameWithoutExtension(track.Path).Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group =>
            {
                var list = group
                    .OrderByDescending(track => track.IsPerfect)
                    .ThenByDescending(track => track.Liked)
                    .ThenByDescending(track => track.Bitrate)
                    .ThenByDescending(track => track.BitsPerSample)
                    .ThenByDescending(track => track.FileSize)
                    .ThenByDescending(track => track.Added)
                    .ToList();
                return new DuplicateFileGroup
                {
                    Name = group.Key,
                    Tracks = list,
                    Keeper = list[0]
                };
            })
            .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public MusicTrack? GetTrackByPath(string path)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {TrackColumns} FROM music_tracks WHERE path=$path";
            command.Parameters.AddWithValue("$path", path);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadTrack(reader) : null;
        }
    }

    /// <summary>清理重复项后更新曲目位置与保护标记。</summary>
    public void UpdateTrackLocation(long id, string newPath, bool isPerfect, bool liked)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE OR REPLACE music_tracks
                SET path=$path, is_perfect=$perfect, liked=$liked, missing=0, seen=1
                WHERE id=$id;
                """;
            command.Parameters.AddWithValue("$path", newPath);
            command.Parameters.AddWithValue("$perfect", isPerfect ? 1 : 0);
            command.Parameters.AddWithValue("$liked", liked ? 1 : 0);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    private static bool IsUnderLibrary(string path, string root)
    {
        string cleanRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(path, cleanRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        string prefix = cleanRoot + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // ================= 喜欢 / 评分 / 统计 =================

    public void SetLiked(long id, bool liked)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE music_tracks SET liked=$liked WHERE id=$id";
            command.Parameters.AddWithValue("$liked", liked ? 1 : 0);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public void SetRating(long id, int rating)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE music_tracks SET rating=$rating WHERE id=$id";
            command.Parameters.AddWithValue("$rating", Math.Clamp(rating, 0, 5));
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public void RecordPlay(long id, double seconds)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE music_tracks
                SET play_count=play_count+1, last_played=$now
                WHERE id=$id;
                INSERT INTO music_history(track_id,played_at,seconds) VALUES($id,$now,$seconds);
                """;
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$seconds", Math.Max(0, seconds));
            command.ExecuteNonQuery();
        }
    }

    public int GetLikedCount()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM music_tracks WHERE missing=0 AND liked=1";
            return Convert.ToInt32(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        }
    }

    public MusicStatistics GetStatistics()
    {
        lock (_gate)
        {
            using var connection = Open();
            var stats = new MusicStatistics();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT COUNT(*), COUNT(DISTINCT album), COUNT(DISTINCT artist), COALESCE(SUM(duration),0),
                           COALESCE(SUM(play_count),0)
                    FROM music_tracks WHERE missing=0;
                    """;
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    stats.TrackCount = reader.GetInt32(0);
                    stats.AlbumCount = reader.GetInt32(1);
                    stats.ArtistCount = reader.GetInt32(2);
                    stats.TotalSeconds = reader.GetDouble(3);
                    stats.TotalPlays = reader.GetInt32(4);
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COALESCE(SUM(seconds),0) FROM music_history";
                stats.PlayedSeconds = Convert.ToDouble(command.ExecuteScalar() ?? 0d, CultureInfo.InvariantCulture);
            }
            stats.TopTracks = ReadStats(connection,
                """
                SELECT title, play_count, play_count*duration FROM music_tracks
                WHERE play_count>0 ORDER BY play_count DESC LIMIT 10;
                """);
            stats.TopArtists = ReadStats(connection,
                """
                SELECT CASE WHEN artist<>'' THEN artist ELSE '未知艺术家' END AS a, SUM(play_count),
                       SUM(play_count*duration)
                FROM music_tracks WHERE play_count>0 GROUP BY a ORDER BY SUM(play_count) DESC LIMIT 10;
                """);
            stats.TopAlbums = ReadStats(connection,
                """
                SELECT CASE WHEN album<>'' THEN album ELSE '未知专辑' END AS a, SUM(play_count),
                       SUM(play_count*duration)
                FROM music_tracks WHERE play_count>0 GROUP BY a ORDER BY SUM(play_count) DESC LIMIT 10;
                """);
            return stats;
        }
    }

    private static List<MusicPlayStat> ReadStats(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var list = new List<MusicPlayStat>();
        while (reader.Read())
        {
            list.Add(new MusicPlayStat
            {
                Name = reader.GetString(0),
                Plays = reader.GetInt32(1),
                Seconds = reader.GetDouble(2)
            });
        }
        return list;
    }

    // ================= 播放列表 =================

    public List<MusicPlaylist> GetPlaylists()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.id,p.name,p.created,p.updated,
                       (SELECT COUNT(*) FROM music_playlist_tracks pt WHERE pt.playlist_id=p.id)
                FROM music_playlists p ORDER BY p.name COLLATE NOCASE;
                """;
            using var reader = command.ExecuteReader();
            var list = new List<MusicPlaylist>();
            while (reader.Read())
            {
                list.Add(new MusicPlaylist
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Created = reader.GetInt64(2),
                    Updated = reader.GetInt64(3),
                    TrackCount = reader.GetInt32(4)
                });
            }
            return list;
        }
    }

    public long CreatePlaylist(string name)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO music_playlists(name,created,updated) VALUES($name,$now,$now);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(name) ? "新建播放列表" : name.Trim());
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
    }

    public void RenamePlaylist(long id, string name)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE music_playlists SET name=$name,updated=$now WHERE id=$id";
            command.Parameters.AddWithValue("$name", name.Trim());
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public void DeletePlaylist(long id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM music_playlist_tracks WHERE playlist_id=$id;
                DELETE FROM music_playlists WHERE id=$id;
                """;
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public void AddToPlaylist(long playlistId, IEnumerable<long> trackIds)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            long position = 0;
            using (var positionCommand = connection.CreateCommand())
            {
                positionCommand.Transaction = transaction;
                positionCommand.CommandText =
                    "SELECT COALESCE(MAX(position),0) FROM music_playlist_tracks WHERE playlist_id=$id";
                positionCommand.Parameters.AddWithValue("$id", playlistId);
                position = Convert.ToInt64(positionCommand.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
            }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO music_playlist_tracks(playlist_id,track_id,position)
                VALUES($id,$track,$position);
                """;
            var idParameter = command.Parameters.Add("$id", SqliteType.Integer);
            var trackParameter = command.Parameters.Add("$track", SqliteType.Integer);
            var positionParameter = command.Parameters.Add("$position", SqliteType.Integer);
            idParameter.Value = playlistId;
            foreach (long trackId in trackIds)
            {
                trackParameter.Value = trackId;
                positionParameter.Value = ++position;
                command.ExecuteNonQuery();
            }
            using var touch = connection.CreateCommand();
            touch.Transaction = transaction;
            touch.CommandText = "UPDATE music_playlists SET updated=$now WHERE id=$id";
            touch.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            touch.Parameters.AddWithValue("$id", playlistId);
            touch.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    public void RemoveFromPlaylist(long playlistId, long trackId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "DELETE FROM music_playlist_tracks WHERE playlist_id=$id AND track_id=$track";
            command.Parameters.AddWithValue("$id", playlistId);
            command.Parameters.AddWithValue("$track", trackId);
            command.ExecuteNonQuery();
        }
    }

    // ================= 标签编辑 =================

    public void UpdateTrackMetadata(MusicTrack track)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE music_tracks SET title=$title,artist=$artist,album_artist=$albumArtist,
                    album=$album,genre=$genre,year=$year,track_no=$trackNo,disc_no=$discNo,
                    rating=$rating,comment=$comment,modified=$modified
                WHERE id=$id;
                """;
            command.Parameters.AddWithValue("$title", track.Title ?? "");
            command.Parameters.AddWithValue("$artist", track.Artist ?? "");
            command.Parameters.AddWithValue("$albumArtist", track.AlbumArtist ?? "");
            command.Parameters.AddWithValue("$album", track.Album ?? "");
            command.Parameters.AddWithValue("$genre", track.Genre ?? "");
            command.Parameters.AddWithValue("$year", track.Year);
            command.Parameters.AddWithValue("$trackNo", track.TrackNo);
            command.Parameters.AddWithValue("$discNo", track.DiscNo);
            command.Parameters.AddWithValue("$rating", track.Rating);
            command.Parameters.AddWithValue("$comment", track.Comment ?? "");
            command.Parameters.AddWithValue("$modified", track.Modified);
            command.Parameters.AddWithValue("$id", track.Id);
            command.ExecuteNonQuery();
        }
    }

    public void RemoveTrack(long id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM music_playlist_tracks WHERE track_id=$id;
                DELETE FROM music_history WHERE track_id=$id;
                DELETE FROM music_tracks WHERE id=$id;
                """;
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    // ================= 元数据 =================

    public string? GetMeta(string key)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM music_meta WHERE key=$key";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string;
        }
    }

    public void SetMeta(string key, string value)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO music_meta(key,value) VALUES($key,$value)
                ON CONFLICT(key) DO UPDATE SET value=excluded.value;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value ?? "");
            command.ExecuteNonQuery();
        }
    }

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    private static MusicTrack ReadTrack(SqliteDataReader reader)
    {
        return new MusicTrack
        {
            Id = reader.GetInt64(0),
            Path = reader.GetString(1),
            Title = reader.GetString(2),
            Artist = reader.GetString(3),
            AlbumArtist = reader.GetString(4),
            Album = reader.GetString(5),
            Genre = reader.GetString(6),
            Year = reader.GetInt32(7),
            TrackNo = reader.GetInt32(8),
            DiscNo = reader.GetInt32(9),
            DurationSeconds = reader.GetDouble(10),
            Bitrate = reader.GetInt32(11),
            SampleRate = reader.GetInt32(12),
            BitsPerSample = reader.GetInt32(13),
            Channels = reader.GetInt32(14),
            Codec = reader.GetString(15),
            FileSize = reader.GetInt64(16),
            CoverKey = reader.GetString(17),
            Liked = reader.GetInt32(18) != 0,
            Rating = reader.GetInt32(19),
            PlayCount = reader.GetInt32(20),
            LastPlayed = reader.GetInt64(21),
            Added = reader.GetInt64(22),
            Modified = reader.GetInt64(23),
            IsPerfect = reader.GetInt32(24) != 0,
            Missing = reader.GetInt32(25) != 0,
            GainDb = reader.GetDouble(26),
            Comment = reader.GetString(27)
        };
    }
}
