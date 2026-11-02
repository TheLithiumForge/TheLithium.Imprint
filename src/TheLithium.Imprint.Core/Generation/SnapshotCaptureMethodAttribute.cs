namespace TheLithium.Imprint.Generation;

/// <summary>
/// Marks a capture method outside <see cref="SnapshotExtensions"/>, such as <c>AssertImage</c>. The MSBuild task wraps
/// every test that reaches one, and the generator registers it, so the test gets a scope and pruning knows its folder.
/// </summary>
/// <remarks>Matched by name in the build task and the generator. Keep the full name stable.</remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class SnapshotCaptureMethodAttribute : Attribute;
