using System.Text.Json;
using Client.Core;
using Microsoft.Data.Sqlite;

namespace Client.Storage;

public sealed class ProfileRepository(ClientDatabase database)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task UpsertAsync(ProxyProfile profile, CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO profiles (id, name, host, port, source, subscription_url, json, updated_at)
            VALUES ($id, $name, $host, $port, $source, $subscriptionUrl, $json, $updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                host = excluded.host,
                port = excluded.port,
                source = excluded.source,
                subscription_url = excluded.subscription_url,
                json = excluded.json,
                updated_at = excluded.updated_at;
            """;
        Add(command, "$id", profile.Id);
        Add(command, "$name", profile.Name);
        Add(command, "$host", profile.Host);
        Add(command, "$port", profile.Port);
        Add(command, "$source", profile.Source);
        Add(command, "$subscriptionUrl", (object?)profile.SubscriptionUrl ?? DBNull.Value);
        Add(command, "$json", JsonSerializer.Serialize(profile, JsonOptions));
        Add(command, "$updatedAt", profile.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProxyProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        var profiles = new List<ProxyProfile>();
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM profiles ORDER BY updated_at DESC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var profile = JsonSerializer.Deserialize<ProxyProfile>(json, JsonOptions);
            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return profiles;
    }

    public async Task<ProxyProfile?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM profiles WHERE id = $id;";
        Add(command, "$id", id);
        var json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return json is null ? null : JsonSerializer.Deserialize<ProxyProfile>(json, JsonOptions);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM profiles WHERE id = $id;";
        Add(command, "$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Add(SqliteCommand command, string name, object value)
    {
        command.Parameters.AddWithValue(name, value);
    }
}

