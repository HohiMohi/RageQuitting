using Unity.Pipeline.Commands;

namespace RageQuitting.Editor.AgentHarness
{
    internal static class ValidationPipelineCommands
    {
        [CliCommand(
            "rq_quick_validators",
            "Run the two isolated RageQuitting quick validators and return structured results.",
            MainThreadRequired = true,
            Tags = new[] { "ragequitting/validation" })]
        public static QuickValidatorsResult RunQuickValidators()
        {
            return QuickValidatorsSuite.Validate();
        }

        [CliCommand(
            "rq_validation_context",
            "Return the read-only Unity Editor context used by the RageQuitting validation harness.",
            MainThreadRequired = true,
            Tags = new[] { "ragequitting/validation" })]
        public static ValidationEditorContext GetValidationContext()
        {
            return ValidationEditorContextService.Capture();
        }
    }
}
