// <copyright file="MLModelStorage.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Analytics.ML;

/// <summary>
/// Storage interface for machine learning models in SharpCoreDB.
/// Provides CRUD operations for model persistence and versioning.
/// C# 14: Async patterns, collection expressions.
/// </summary>
public sealed class MLModelStorage
{
    private readonly IDatabase _database;
    private readonly string _modelsTable = "ml_models";
    private readonly string _versionsTable = "ml_model_versions";

    /// <summary>
    /// Initializes a new instance of the <see cref="MLModelStorage"/> class.
    /// </summary>
    /// <param name="database">The SharpCoreDB database instance.</param>
    public MLModelStorage(IDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        InitializeTables();
    }

    /// <summary>
    /// Stores a new machine learning model.
    /// </summary>
    /// <param name="model">The model to store.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StoreModelAsync(MLModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var sql = $@"
            INSERT INTO {_modelsTable} (
                model_id, name, version, model_type, description,
                input_features, output_features, parameters_json,
                model_data, created_at, modified_at, metrics, tags
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

        var parameters = new Dictionary<string, object?>
        {
            ["model_id"] = model.ModelId,
            ["name"] = model.Name,
            ["version"] = model.Version,
            ["model_type"] = model.ModelType,
            ["description"] = model.Description,
            ["input_features"] = SerializeFeatures(model.InputFeatures),
            ["output_features"] = SerializeFeatures(model.OutputFeatures),
            ["parameters_json"] = model.ParametersJson,
            ["model_data"] = model.ModelData,
            ["created_at"] = model.CreatedAt.ToUnixTimeMilliseconds(),
            ["modified_at"] = model.ModifiedAt.ToUnixTimeMilliseconds(),
            ["metrics"] = model.Metrics is not null ? SerializeMetrics(model.Metrics) : null,
            ["tags"] = string.Join(",", model.Tags)
        };

        await Task.Run(() => _database.ExecuteSQL(sql, parameters));
    }

    /// <summary>
    /// Retrieves a model by its identifier.
    /// </summary>
    /// <param name="modelId">The model identifier.</param>
    /// <param name="version">Optional specific version.</param>
    /// <returns>The model, or null if not found.</returns>
    public async Task<MLModel?> GetModelAsync(string modelId, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var sql = $@"
            SELECT model_id, name, version, model_type, description,
                   input_features, output_features, parameters_json,
                   model_data, created_at, modified_at, metrics, tags
            FROM {_modelsTable}
            WHERE model_id = ?
            {(version is not null ? "AND version = ?" : "")}
            ORDER BY version DESC
            LIMIT 1";

        var parameters = new Dictionary<string, object?> { ["model_id"] = modelId };
        if (version is not null)
        {
            parameters["version"] = version;
        }

        var rows = await Task.Run(() => _database.ExecuteQuery(sql, parameters));

        if (rows.Count == 0)
        {
            return null;
        }

        var row = rows[0];
        return DeserializeModel(row);
    }

    /// <summary>
    /// Lists all models with optional filtering.
    /// </summary>
    /// <param name="modelType">Optional model type filter.</param>
    /// <param name="tags">Optional tags filter.</param>
    /// <returns>A list of models.</returns>
    public async Task<IReadOnlyList<MLModel>> ListModelsAsync(string? modelType = null, IReadOnlyList<string>? tags = null)
    {
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object?>();

        if (modelType is not null)
        {
            conditions.Add("model_type = ?");
            parameters["model_type"] = modelType;
        }

        if (tags is not null && tags.Count > 0)
        {
            var tagConditions = tags.Select((_, i) => $"tags LIKE ?").ToList();
            conditions.Add($"({string.Join(" OR ", tagConditions)})");
            for (int i = 0; i < tags.Count; i++)
            {
                parameters[$"tag_{i}"] = $"%{tags[i]}%";
            }
        }

        var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";
        var sql = $@"
            SELECT model_id, name, version, model_type, description,
                   input_features, output_features, parameters_json,
                   model_data, created_at, modified_at, metrics, tags
            FROM {_modelsTable}
            {whereClause}
            ORDER BY name, version DESC";

        var rows = await Task.Run(() => _database.ExecuteQuery(sql, parameters));
        return rows.Select(DeserializeModel).ToList();
    }

    /// <summary>
    /// Updates an existing model.
    /// </summary>
    /// <param name="model">The updated model.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UpdateModelAsync(MLModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var sql = $@"
            UPDATE {_modelsTable}
            SET name = ?, description = ?, parameters_json = ?,
                model_data = ?, modified_at = ?, metrics = ?, tags = ?
            WHERE model_id = ? AND version = ?";

        var parameters = new Dictionary<string, object?>
        {
            ["name"] = model.Name,
            ["description"] = model.Description,
            ["parameters_json"] = model.ParametersJson,
            ["model_data"] = model.ModelData,
            ["modified_at"] = model.ModifiedAt.ToUnixTimeMilliseconds(),
            ["metrics"] = model.Metrics is not null ? SerializeMetrics(model.Metrics) : null,
            ["tags"] = string.Join(",", model.Tags),
            ["model_id"] = model.ModelId,
            ["version"] = model.Version
        };

        await Task.Run(() => _database.ExecuteSQL(sql, parameters));
    }

    /// <summary>
    /// Deletes a model.
    /// </summary>
    /// <param name="modelId">The model identifier.</param>
    /// <param name="version">Optional specific version to delete.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DeleteModelAsync(string modelId, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var sql = $@"
            DELETE FROM {_modelsTable}
            WHERE model_id = ?
            {(version is not null ? "AND version = ?" : "")}";

        var parameters = new Dictionary<string, object?> { ["model_id"] = modelId };
        if (version is not null)
        {
            parameters["version"] = version;
        }

        await Task.Run(() => _database.ExecuteSQL(sql, parameters));
    }

    /// <summary>
    /// Initializes the required database tables.
    /// </summary>
    private void InitializeTables()
    {
        var createModelsTable = $@"
            CREATE TABLE IF NOT EXISTS {_modelsTable} (
                model_id TEXT NOT NULL,
                name TEXT NOT NULL,
                version TEXT NOT NULL,
                model_type TEXT NOT NULL,
                description TEXT,
                input_features TEXT NOT NULL,
                output_features TEXT NOT NULL,
                parameters_json TEXT NOT NULL,
                model_data BLOB NOT NULL,
                created_at INTEGER NOT NULL,
                modified_at INTEGER NOT NULL,
                metrics TEXT,
                tags TEXT,
                PRIMARY KEY (model_id, version)
            )";

        var createVersionsTable = $@"
            CREATE TABLE IF NOT EXISTS {_versionsTable} (
                model_id TEXT NOT NULL,
                version TEXT NOT NULL,
                parent_version TEXT,
                change_description TEXT,
                created_at INTEGER NOT NULL,
                PRIMARY KEY (model_id, version),
                FOREIGN KEY (model_id) REFERENCES {_modelsTable}(model_id)
            )";

        _database.ExecuteSQL(createModelsTable);
        _database.ExecuteSQL(createVersionsTable);
    }

    /// <summary>
    /// Serializes feature schemas to JSON.
    /// </summary>
    /// <param name="features">The features to serialize.</param>
    /// <returns>The JSON string.</returns>
    private static string SerializeFeatures(IReadOnlyList<FeatureSchema> features)
    {
        return System.Text.Json.JsonSerializer.Serialize(features);
    }

    /// <summary>
    /// Deserializes feature schemas from JSON.
    /// </summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The feature schemas.</returns>
    private static IReadOnlyList<FeatureSchema> DeserializeFeatures(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<IReadOnlyList<FeatureSchema>>(json) ?? [];
    }

    /// <summary>
    /// Serializes model metrics to JSON.
    /// </summary>
    /// <param name="metrics">The metrics to serialize.</param>
    /// <returns>The JSON string.</returns>
    private static string SerializeMetrics(ModelMetrics metrics)
    {
        return System.Text.Json.JsonSerializer.Serialize(metrics);
    }

    /// <summary>
    /// Deserializes model metrics from JSON.
    /// </summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The model metrics.</returns>
    private static ModelMetrics? DeserializeMetrics(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<ModelMetrics>(json);
    }

    /// <summary>
    /// Deserializes a model from a database row.
    /// </summary>
    /// <param name="row">The database row.</param>
    /// <returns>The deserialized model.</returns>
    private static MLModel DeserializeModel(Dictionary<string, object> row)
    {
        return new MLModel
        {
            ModelId = (string)row["model_id"],
            Name = (string)row["name"],
            Version = (string)row["version"],
            ModelType = (string)row["model_type"],
            Description = row.GetValueOrDefault("description") as string,
            InputFeatures = DeserializeFeatures((string)row["input_features"]),
            OutputFeatures = DeserializeFeatures((string)row["output_features"]),
            ParametersJson = (string)row["parameters_json"],
            ModelData = (byte[])row["model_data"],
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)row["created_at"]),
            ModifiedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)row["modified_at"]),
            Metrics = row.GetValueOrDefault("metrics") is string metricsJson
                ? DeserializeMetrics(metricsJson)
                : null,
            Tags = ((string?)row.GetValueOrDefault("tags"))?.Split(',') ?? []
        };
    }
}
