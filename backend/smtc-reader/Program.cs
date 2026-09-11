using System.Text;
using Windows.Media.Control;
using Windows.Storage.Streams;

Console.OutputEncoding = Encoding.UTF8;

bool includeImage = args.Contains("--image");

try
{
    var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    var session = manager.GetCurrentSession();

    if (session is null)
    {
        Console.WriteLine("{\"playing\":false}");
        return;
    }

    var props = await session.TryGetMediaPropertiesAsync();
    var playback = session.GetPlaybackInfo();
    var timeline = session.GetTimelineProperties();

    string? title = props.Title;
    string? artist = !string.IsNullOrWhiteSpace(props.Artist) ? props.Artist : props.AlbumArtist;

    if (string.IsNullOrWhiteSpace(title))
    {
        Console.WriteLine("{\"playing\":false}");
        return;
    }

    bool isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    // SMTC solo actualiza Position en eventos (play/pause/seek/track change),
    // no de forma continua. Interpolamos usando LastUpdatedTime.
    double rate = playback.PlaybackRate ?? 1.0;
    double durationMs = (timeline.EndTime - timeline.StartTime).TotalMilliseconds;
    double baseMs = timeline.Position.TotalMilliseconds;
    double elapsedSinceUpdateMs = (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalMilliseconds;

    double adjustedMs = baseMs;
    if (isPlaying && elapsedSinceUpdateMs > 0)
    {
        adjustedMs += elapsedSinceUpdateMs * rate;
    }

    long progressMs = (long)Math.Max(0, Math.Min(Math.Round(adjustedMs), durationMs));

    string? image = null;
    if (includeImage && props.Thumbnail is not null)
    {
        try
        {
            using var stream = await props.Thumbnail.OpenReadAsync();
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);

            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);

            image = $"data:{stream.ContentType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            image = null;
        }
    }

    var json = new StringBuilder();
    json.Append('{');
    json.Append("\"playing\":").Append(isPlaying ? "true" : "false").Append(',');
    json.Append("\"title\":").Append(JsonString(title)).Append(',');
    json.Append("\"artist\":").Append(JsonString(artist ?? "")).Append(',');
    json.Append("\"progress\":").Append(progressMs).Append(',');
    json.Append("\"duration\":").Append((long)durationMs).Append(',');
    json.Append("\"source\":").Append(JsonString(session.SourceAppUserModelId ?? ""));
    if (image is not null)
    {
        json.Append(",\"image\":").Append(JsonString(image));
    }
    json.Append('}');

    Console.WriteLine(json.ToString());
}
catch
{
    Console.WriteLine("{\"playing\":false}");
}

static string JsonString(string s)
{
    var sb = new StringBuilder();
    sb.Append('"');
    foreach (char c in s)
    {
        switch (c)
        {
            case '"': sb.Append("\\\""); break;
            case '\\': sb.Append("\\\\"); break;
            case '\n': sb.Append("\\n"); break;
            case '\r': sb.Append("\\r"); break;
            case '\t': sb.Append("\\t"); break;
            default:
                if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
                break;
        }
    }
    sb.Append('"');
    return sb.ToString();
}
