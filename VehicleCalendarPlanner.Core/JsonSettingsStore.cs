using System.Text.Json;

namespace VehicleCalendarPlanner.Core;

public sealed class JsonSettingsStore(string path)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<PlannerSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
                return new PlannerSettings();

            await using var stream = File.OpenRead(path);
            var settings = await JsonSerializer.DeserializeAsync<PlannerSettings>(stream, Options, cancellationToken)
                ?? throw new InvalidDataException("The saved family settings are empty.");
            if (settings.People is null || settings.Vehicles is null)
                throw new InvalidDataException("The saved family settings are incomplete.");
            return settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PlannerSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("A settings directory is required."));
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
