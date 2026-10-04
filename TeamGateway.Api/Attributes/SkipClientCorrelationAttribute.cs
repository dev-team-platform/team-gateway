namespace TeamGateway.Api.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipClientCorrelationAttribute : Attribute
{
}