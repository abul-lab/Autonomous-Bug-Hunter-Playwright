using System.Text.RegularExpressions;

namespace BugHunter.Api.FailureIntelligence;

/// <summary>
/// Deterministic and heuristic classifier for normalizing and categorizing Playwright test failure telemetry.
/// </summary>
public static partial class FailureClassifier
{
    private static readonly Regex GuidRegex = GeneratedGuidRegex();
    private static readonly Regex TimestampRegex = GeneratedTimestampRegex();
    private static readonly Regex HexAddressRegex = GeneratedHexAddressRegex();
    private static readonly Regex LineColRegex = GeneratedLineColRegex();

    public static NormalizedError Classify(IngestedFailure failure)
    {
        var rawMsg = failure.ErrorMessage ?? string.Empty;
        var stack = failure.StackTrace ?? string.Empty;
        var consoleLogs = failure.Telemetry?.ConsoleErrors ?? [];
        var netErrors = failure.Telemetry?.NetworkErrors ?? [];

        var cleaned = CleanErrorMessage(rawMsg);
        var tokens = ExtractSalientTokens(cleaned);

        // Rule 1: Authentication Failure (401, 403, JWT, session)
        if (netErrors.Any(n => n.StatusCode is 401 or 403) ||
            rawMsg.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("403", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("Token expired", StringComparison.OrdinalIgnoreCase))
        {
            return new NormalizedError
            {
                Signature = GenerateSignature("Auth", tokens),
                CleanedMessage = cleaned,
                SalientTokens = tokens,
                InferredCategory = FailureCategory.AuthenticationFailure,
                Confidence = 0.95,
                DiagnosticSummary = "Session authorization expired or rejected by API gateway / RBAC boundary."
            };
        }

        // Rule 2: Environment / Infrastructure (ECONNREFUSED, 502, 503, Gateway Timeout)
        if (netErrors.Any(n => n.StatusCode is 502 or 503 or 504) ||
            rawMsg.Contains("ECONNREFUSED", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("ERR_CONNECTION_REFUSED", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("Bad Gateway", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return new NormalizedError
            {
                Signature = GenerateSignature("Env", tokens),
                CleanedMessage = cleaned,
                SalientTokens = tokens,
                InferredCategory = FailureCategory.EnvironmentFailure,
                Confidence = 0.92,
                DiagnosticSummary = "Application target host or downstream proxy is unreachable."
            };
        }

        // Rule 3: Backend / API Failure (500 Internal Server Error / 5xx)
        if (netErrors.Any(n => n.StatusCode >= 500) ||
            rawMsg.Contains("500", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("Internal Server Error", StringComparison.OrdinalIgnoreCase) ||
            consoleLogs.Any(c => c.Contains("500", StringComparison.OrdinalIgnoreCase)))
        {
            return new NormalizedError
            {
                Signature = GenerateSignature("Api500", tokens),
                CleanedMessage = cleaned,
                SalientTokens = tokens,
                InferredCategory = FailureCategory.BackendApiFailure,
                Confidence = 0.90,
                DiagnosticSummary = "Server-side unhandled exception returned during workflow execution."
            };
        }

        // Rule 4: Visual Regression
        if (rawMsg.Contains("Screenshot comparison failed", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("Pixel difference", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("visual difference threshold", StringComparison.OrdinalIgnoreCase))
        {
            return new NormalizedError
            {
                Signature = GenerateSignature("Visual", tokens),
                CleanedMessage = cleaned,
                SalientTokens = tokens,
                InferredCategory = FailureCategory.VisualRegression,
                Confidence = 0.96,
                DiagnosticSummary = "Visual regression threshold exceeded against baseline snapshot."
            };
        }

        // Rule 5: Test Data Collision / Seed Failure
        if (rawMsg.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("foreign key constraint", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("out of stock", StringComparison.OrdinalIgnoreCase))
        {
            return new NormalizedError
            {
                Signature = GenerateSignature("TestData", tokens),
                CleanedMessage = cleaned,
                SalientTokens = tokens,
                InferredCategory = FailureCategory.TestDataFailure,
                Confidence = 0.88,
                DiagnosticSummary = "Stale or conflicting test fixture data in application state."
            };
        }

        // Rule 6: Locator Drift vs Timing Issue
        // Playwright locator timeout vs execution timeout
        if (rawMsg.Contains("Timeout", StringComparison.OrdinalIgnoreCase) &&
            (rawMsg.Contains("waiting for locator", StringComparison.OrdinalIgnoreCase) ||
             rawMsg.Contains("waiting for getBy", StringComparison.OrdinalIgnoreCase) ||
             !string.IsNullOrEmpty(failure.FailedLocator)))
        {
            // If DOM was inspected and element was completely missing or replaced
            return new NormalizedError
            {
                Signature = GenerateSignature("LocatorDrift", tokens),
                CleanedMessage = cleaned,
                SalientTokens = tokens,
                InferredCategory = FailureCategory.LocatorDrift,
                Confidence = 0.89,
                DiagnosticSummary = $"Target element '{failure.FailedLocator}' not resolved in active DOM. Selector drift detected."
            };
        }

        if (rawMsg.Contains("Target closed", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("Navigation timeout", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("detached from the DOM", StringComparison.OrdinalIgnoreCase))
        {
            return new NormalizedError
            {
                Signature = GenerateSignature("Timing", tokens),
                CleanedMessage = cleaned,
                SalientTokens = tokens,
                InferredCategory = FailureCategory.TimingIssue,
                Confidence = 0.85,
                DiagnosticSummary = "DOM state mutation race condition or page navigation timeout."
            };
        }

        // Rule 7: Assertion Failure
        if (rawMsg.Contains("expect(", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("AssertionError", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("invariant:", StringComparison.OrdinalIgnoreCase) ||
            rawMsg.Contains("Received:", StringComparison.OrdinalIgnoreCase))
        {
            return new NormalizedError
            {
                Signature = GenerateSignature("Assert", tokens),
                CleanedMessage = cleaned,
                SalientTokens = tokens,
                InferredCategory = FailureCategory.AssertionFailure,
                Confidence = 0.94,
                DiagnosticSummary = "Functional assertion or invariant rule evaluated to false."
            };
        }

        return new NormalizedError
        {
            Signature = GenerateSignature("Unknown", tokens),
            CleanedMessage = cleaned,
            SalientTokens = tokens,
            InferredCategory = FailureCategory.Unknown,
            Confidence = 0.50,
            DiagnosticSummary = "Unclassified test error."
        };
    }

    public static string CleanErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        var text = GuidRegex.Replace(message, "<GUID>");
        text = TimestampRegex.Replace(text, "<TIMESTAMP>");
        text = HexAddressRegex.Replace(text, "<HEX>");
        text = LineColRegex.Replace(text, "<LOC>");
        return text.Trim();
    }

    public static List<string> ExtractSalientTokens(string cleaned)
    {
        var words = Regex.Split(cleaned.ToLowerInvariant(), @"[^a-z0-9_]+")
                         .Where(w => w.Length is > 2 and < 30)
                         .Where(w => !StopWords.Contains(w))
                         .Distinct()
                         .Take(15)
                         .ToList();
        return words;
    }

    private static string GenerateSignature(string prefix, List<string> tokens)
    {
        var hashSource = $"{prefix}:{string.Join("|", tokens.OrderBy(t => t))}";
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hashSource));
        return $"{prefix}_{Convert.ToHexString(bytes)[..10]}";
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "error", "failed", "waiting",
        "locator", "timeout", "call", "log", "page", "test", "expect", "received", "expected"
    };

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GeneratedGuidRegex();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z?")]
    private static partial Regex GeneratedTimestampRegex();

    [GeneratedRegex(@"0x[0-9a-fA-F]+")]
    private static partial Regex GeneratedHexAddressRegex();

    [GeneratedRegex(@":\d+:\d+")]
    private static partial Regex GeneratedLineColRegex();
}
