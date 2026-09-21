using Microsoft.AspNetCore.Http;

namespace PE_GOL.Tests.Helpers;

/// <summary>
/// Implementación en memoria de ISession para los tests de SesionService (HU-045).
/// Las extensiones SetString/GetString de ISession usan Set(key, byte[]) y
/// TryGetValue(key, out byte[]) → esta implementación las soporta sin pipeline HTTP.
/// </summary>
public class FakeSession : ISession
{
    private readonly Dictionary<string, byte[]> _data = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAvailable => true;
    public string Id => Guid.NewGuid().ToString();
    public IEnumerable<string> Keys => _data.Keys;

    public void Clear() => _data.Clear();

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Remove(string key) => _data.Remove(key);

    public void Set(string key, byte[] value) => _data[key] = value;

    public bool TryGetValue(string key, out byte[] value) => _data.TryGetValue(key, out value!);
}