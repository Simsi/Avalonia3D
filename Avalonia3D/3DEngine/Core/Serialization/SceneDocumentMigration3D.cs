using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Serialization;

public interface ISceneDocumentMigration3D
{
    int SourceVersion { get; }
    int TargetVersion { get; }
    SceneDocument3D Migrate(SceneDocument3D source);
}

public sealed class SceneDocumentMigrationRegistry3D
{
    private readonly object _gate = new();
    private readonly Dictionary<int, ISceneDocumentMigration3D> _migrations = new();

    public void Register(ISceneDocumentMigration3D migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        if (migration.SourceVersion <= 0 || migration.SourceVersion == int.MaxValue || migration.TargetVersion != migration.SourceVersion + 1)
            throw new ArgumentException("Scene migrations must advance exactly one positive version.", nameof(migration));
        lock (_gate)
        {
            if (!_migrations.TryAdd(migration.SourceVersion, migration))
                throw new InvalidOperationException($"A scene migration from version {migration.SourceVersion} is already registered.");
        }
    }

    public SceneDocument3D Upgrade(SceneDocument3D document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version <= 0) throw new InvalidOperationException($"Scene document version {document.Version} is invalid.");
        if (document.Version > SceneDocument3D.CurrentVersion)
            throw new InvalidOperationException($"Scene document version {document.Version} is newer than supported version {SceneDocument3D.CurrentVersion}.");
        while (document.Version < SceneDocument3D.CurrentVersion)
        {
            ISceneDocumentMigration3D migration;
            lock (_gate)
            {
                if (!_migrations.TryGetValue(document.Version, out migration!))
                    throw new InvalidOperationException($"No scene migration is registered from version {document.Version} to {document.Version + 1}.");
            }
            document = migration.Migrate(document) ?? throw new InvalidOperationException($"Scene migration from version {migration.SourceVersion} returned null.");
            if (document.Version != migration.TargetVersion)
                throw new InvalidOperationException($"Scene migration from version {migration.SourceVersion} produced version {document.Version}, expected {migration.TargetVersion}.");
        }
        return document;
    }
}
