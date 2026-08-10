using Microsoft.AspNetCore.DataProtection;
using ShrinkFrame.Application;
using ShrinkFrame.Domain;
using ShrinkFrame.Infrastructure.Immich;

namespace ShrinkFrame.Infrastructure.Tests;

[TestClass]
public sealed class ImmichConnectionTests
{
    [TestMethod]
    [DataRow("https://immich.example", "https://immich.example/")]
    [DataRow("https://immich.example/api/", "https://immich.example/")]
    [DataRow("http://192.168.1.20:2283/api", "http://192.168.1.20:2283/")]
    public void NormalizeUrlAcceptsSiteAndApiRoots(string input, string expected)
        => Assert.AreEqual(expected, ImmichConnectionManager.NormalizeUrl(input).AbsoluteUri);

    [TestMethod]
    [DataRow("ftp://immich.example")]
    [DataRow("https://user:password@immich.example")]
    [DataRow("https://immich.example/path")]
    [DataRow("https://immich.example/?key=value")]
    public void NormalizeUrlRejectsUnsafeValues(string input)
        => Assert.ThrowsExactly<ImmichConnectionException>(() => ImmichConnectionManager.NormalizeUrl(input));

    [TestMethod]
    public async Task SavedKeyIsEncryptedAndNeverAppearsInView()
    {
        const string secret = "plain-secret-value-123";
        var repository = new MemoryRepository();
        var manager = CreateManager(repository, new EphemeralDataProtectionProvider());
        var view = await manager.AddAsync(new("Home", "https://immich.example/api", secret, false, true, true));

        Assert.IsTrue(view.HasApiKey);
        Assert.IsFalse(view.ToString().Contains(secret, StringComparison.Ordinal));
        var envelope = repository.Value!.ApiKeyEnvelope!.Payload;
        Assert.IsFalse(Convert.ToBase64String(envelope).Contains(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(secret)), StringComparison.Ordinal));
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(envelope).Contains(secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ChangedKeyRingProducesActionableErrorWithoutSecret()
    {
        const string secret = "do-not-disclose-this";
        var repository = new MemoryRepository();
        await CreateManager(repository, new EphemeralDataProtectionProvider()).AddAsync(
            new("Home", "https://immich.example", secret, false, true, false));

        var exception = await Assert.ThrowsExactlyAsync<ImmichConnectionException>(
            () => CreateManager(repository, new EphemeralDataProtectionProvider()).TestAsync(repository.Value!.Connection.Id));
        Assert.AreEqual("connection.api_key.unavailable", exception.Code);
        Assert.IsFalse(exception.ToString().Contains(secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PersistedKeyRingDecryptsAfterProviderRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shrinkframe-dp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new MemoryRepository();
            var firstProvider = DataProtectionProvider.Create(new DirectoryInfo(directory), x => x.SetApplicationName("ShrinkFrame-test"));
            var added = await CreateManager(repository, firstProvider).AddAsync(
                new("Restart", "http://127.0.0.1:1", "restart-secret", false, true, false));

            var restartedProvider = DataProtectionProvider.Create(new DirectoryInfo(directory), x => x.SetApplicationName("ShrinkFrame-test"));
            var result = await CreateManager(repository, restartedProvider).TestAsync(added.Id);
            Assert.AreEqual("connection.unreachable", result.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CapabilityClassificationKeepsOptionalSourcePermissionsVisible()
    {
        var repository = new MemoryRepository();
        var connection = new ImmichConnection(ConnectionId.New(), "Home", new Uri("https://immich.example/"), false, true, false);
        connection.RecordTest(DateTimeOffset.UtcNow, "3.1.0", CompatibilityResult.Compatible, null,
            "key-id", "ShrinkFrame", "asset.read,asset.download,asset.upload");
        repository.Value = new(connection, new EncryptedSecretEnvelope([1]));

        var views = await CreateManager(repository, new EphemeralDataProtectionProvider()).ListAsync();
        Assert.HasCount(1, views);
        var view = views[0];
        Assert.IsTrue(view.Capabilities.CanUseAsSource);
        Assert.IsFalse(view.Capabilities.CanPublish);
        CollectionAssert.AreEquivalent(new[] { "asset.view", "album.read" }, view.Capabilities.MissingSourcePermissions.ToArray());
        CollectionAssert.AreEquivalent(new[] { "albumAsset.create" }, view.Capabilities.MissingPublishPermissions.ToArray());
    }

    [TestMethod]
    public async Task DeleteInUseOffersStableDisableError()
    {
        var repository = new MemoryRepository { Required = true };
        var manager = CreateManager(repository, new EphemeralDataProtectionProvider());
        var view = await manager.AddAsync(new("Home", "https://immich.example", "key", false, true, false));
        var exception = await Assert.ThrowsExactlyAsync<ImmichConnectionException>(() => manager.DeleteAsync(view.Id));
        Assert.AreEqual("connection.in_use", exception.Code);
        StringAssert.Contains(exception.Message, "Disable");
    }

    private static ImmichConnectionManager CreateManager(MemoryRepository repository, IDataProtectionProvider provider)
        => new(repository, provider, TimeProvider.System, new ImmichConnectionOptions());

    private sealed class MemoryRepository : IImmichConnectionRepository
    {
        public StoredImmichConnection? Value { get; set; }
        public bool Required { get; set; }
        public Task AddAsync(StoredImmichConnection connection, CancellationToken cancellationToken = default) { Value = connection; return Task.CompletedTask; }
        public Task<StoredImmichConnection?> GetAsync(ConnectionId id, CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task UpdateAsync(StoredImmichConnection connection, CancellationToken cancellationToken = default) { Value = connection; return Task.CompletedTask; }
        public Task<IReadOnlyList<StoredImmichConnection>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StoredImmichConnection>>(Value is null ? [] : [Value]);
        public Task SetDefaultAsync(ConnectionId id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> IsRequiredByActiveWorkAsync(ConnectionId id, CancellationToken cancellationToken = default) => Task.FromResult(Required);
        public Task DeleteAsync(ConnectionId id, CancellationToken cancellationToken = default) { Value = null; return Task.CompletedTask; }
    }
}
