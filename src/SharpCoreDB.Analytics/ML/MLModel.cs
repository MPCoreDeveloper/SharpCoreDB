// <copyright file="MLModel.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Text.Json;

namespace SharpCoreDB.Analytics.ML;

/// <summary>
/// Represents a machine learning model stored in SharpCoreDB.
/// Supports model metadata, versioning, and execution capabilities.
/// C# 14: Primary constructors, collection expressions.
/// </summary>
public sealed class MLModel
{
    /// <summary>Gets the unique model identifier.</summary>
    public required string ModelId { get; init; }

    /// <summary>Gets the model name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the model version.</summary>
    public required string Version { get; init; }

    /// <summary>Gets the model type (e.g., "linear_regression", "neural_network").</summary>
    public required string ModelType { get; init; }

    /// <summary>Gets the model description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the input features schema.</summary>
    public required IReadOnlyList<FeatureSchema> InputFeatures { get; init; }

    /// <summary>Gets the output features schema.</summary>
    public required IReadOnlyList<FeatureSchema> OutputFeatures { get; init; }

    /// <summary>Gets the model parameters as JSON.</summary>
    public required string ParametersJson { get; init; }

    /// <summary>Gets the model binary data.</summary>
    public required byte[] ModelData { get; init; }

    /// <summary>Gets the creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the last modified timestamp.</summary>
    public DateTimeOffset ModifiedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the model accuracy metrics.</summary>
    public ModelMetrics? Metrics { get; init; }

    /// <summary>Gets the model tags for categorization.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Deserializes the model parameters.
    /// </summary>
    /// <typeparam name="T">The parameter type.</typeparam>
    /// <returns>The deserialized parameters.</returns>
    public T? GetParameters<T>() where T : class
    {
        if (string.IsNullOrEmpty(ParametersJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(ParametersJson);
    }

    /// <summary>
    /// Creates a new model with updated parameters.
    /// </summary>
    /// <param name="parameters">The new parameters.</param>
    /// <param name="modelData">The new model data.</param>
    /// <param name="metrics">The updated metrics.</param>
    /// <returns>A new model instance.</returns>
    public MLModel WithUpdates(object? parameters = null, byte[]? modelData = null, ModelMetrics? metrics = null)
    {
        var newParametersJson = parameters is not null
            ? JsonSerializer.Serialize(parameters)
            : ParametersJson;

        return new MLModel
        {
            ModelId = ModelId,
            Name = Name,
            Version = Version,
            ModelType = ModelType,
            Description = Description,
            InputFeatures = InputFeatures,
            OutputFeatures = OutputFeatures,
            ParametersJson = newParametersJson,
            ModelData = modelData ?? ModelData,
            CreatedAt = CreatedAt,
            ModifiedAt = DateTimeOffset.UtcNow,
            Metrics = metrics ?? Metrics,
            Tags = Tags
        };
    }
}

/// <summary>
/// Represents a feature in the model schema.
/// </summary>
public sealed class FeatureSchema
{
    /// <summary>Gets the feature name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the feature data type.</summary>
    public required string DataType { get; init; }

    /// <summary>Gets the feature description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets whether the feature is nullable.</summary>
    public bool IsNullable { get; init; }

    /// <summary>Gets the feature constraints.</summary>
    public IReadOnlyDictionary<string, object> Constraints { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Model performance metrics.
/// </summary>
public sealed class ModelMetrics
{
    /// <summary>Gets the accuracy score (0.0 to 1.0).</summary>
    public double Accuracy { get; init; }

    /// <summary>Gets the precision score.</summary>
    public double Precision { get; init; }

    /// <summary>Gets the recall score.</summary>
    public double Recall { get; init; }

    /// <summary>Gets the F1 score.</summary>
    public double F1Score { get; init; }

    /// <summary>Gets the mean squared error (for regression).</summary>
    public double? MeanSquaredError { get; init; }

    /// <summary>Gets the R-squared score (for regression).</summary>
    public double? RSquared { get; init; }

    /// <summary>Gets the training duration.</summary>
    public TimeSpan TrainingDuration { get; init; }

    /// <summary>Gets the number of training samples.</summary>
    public long TrainingSamples { get; init; }

    /// <summary>Gets additional custom metrics.</summary>
    public IReadOnlyDictionary<string, double> CustomMetrics { get; init; } = new Dictionary<string, double>();
}
