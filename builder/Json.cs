using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.Json;

[JsonSerializable(typeof(Source))]
[JsonSerializable(typeof(Release))]
[JsonSerializable(typeof(Release[]))]
[JsonSerializable(typeof(Asset))]
[JsonSerializable(typeof(PackageInfo))]
[JsonSerializable(typeof(RepositoryPackageInfo))]
[JsonSerializable(typeof(RepositorySetting))]
[JsonSourceGenerationOptions(AllowTrailingCommas = true)]
internal sealed partial class SerializeContexts : JsonSerializerContext;

internal sealed record class RepositorySetting
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("author")]
    public required string Author { get; set; }

    [JsonPropertyName("url")]
    public required string Url { get; set; }

    [JsonPropertyName("id")]
    public required string Id { get; set; }
}

internal sealed record class Author
{
    public string? Name { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }
}

internal sealed record class Source
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("url")]
    public required string Url { get; set; }

    [JsonPropertyName("author")]
    [JsonConverter(typeof(AuthorConverter))]
    public required Author Author { get; set; }

    [JsonPropertyName("githubRepos")]
    public string[]? Repositories { get; set; }
}

internal sealed class Release
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("assets")]
    public Asset[]? Assets { get; set; }
}

public sealed class Asset
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("id")]
    public required int Id { get; set; }

    [JsonPropertyName("content_type")]
    public required string ContentType { get; set; }

    [JsonPropertyName("browser_download_url")]
    public required string DownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public ulong Size { get; set; }
}

internal record class PackageInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; set; }

    [JsonPropertyName("version")]
    public required string Version { get; set; }

    [JsonPropertyName("author")]
    [JsonConverter(typeof(AuthorConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Author? Author { get; set; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("unity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unity { get; set; }

    [JsonPropertyName("dependencies")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Dependencies { get; set; }

    [JsonPropertyName("vpmDependencies")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? VpmDependencies { get; set; }

    [JsonPropertyName("legacyFolders")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? LegacyFolders { get; set; }

    [JsonPropertyName("legacyFiles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? LegacyFiles { get; set; }

    [JsonPropertyName("legacyPackages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? LegacyPackages { get; set; }

    [JsonPropertyName("changelogUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChangelogUrl { get; set; }

    [JsonPropertyName("zipSHA256")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ZipSHA256 { get; set; }
}

internal sealed record RepositoryPackageInfo : PackageInfo
{
    [JsonPropertyName("repo")]
    public string? Repository { get; set; }
}

internal sealed class AuthorConverter : JsonConverter<Author>
{
    public static AuthorConverter Instance { get; } = new AuthorConverter();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Author? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new Author() { Name = reader.GetString() };

        return ReadObject(ref reader, typeToConvert, options);
    }

    private static Author? ReadObject(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        var author = new Author();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return author;
            }

            // Get the key.
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }

            string? propertyName = reader.GetString();
            if (propertyName is null)
                throw new JsonException();

            reader.Read();
            if (propertyName.Equals("name", StringComparison.OrdinalIgnoreCase))
                author.Name = reader.GetString();
            else if (propertyName.Equals("url", StringComparison.OrdinalIgnoreCase))
                author.Url = reader.GetString();

        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Author value, JsonSerializerOptions options)
    {
        if (value.Url is null)
        {
            writer.WriteStringValue(value.Name);
        }
        else
        {
            writer.WriteStartObject();
            writer.WriteString("name"u8, value.Name);
            writer.WriteString("url"u8, value.Url);
            writer.WriteEndObject();
        }
    }
}
