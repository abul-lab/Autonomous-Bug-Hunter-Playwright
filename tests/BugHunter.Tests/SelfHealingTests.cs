using BugHunter.Api.SelfHealing;
using Xunit;

namespace BugHunter.Tests;

public class SelfHealingTests
{
    [Fact]
    public void AstHealer_FindCandidates_RanksAccessibilityRoleHighest()
    {
        var intent = new IntentSpec
        {
            OriginalIntent = "Confirm booking",
            FailedLocator = "#legacy-book-btn",
            AxTreeSnippet = "button \"Confirm booking\" [data-testid=\"btn-confirm\"]",
            ActionType = "click"
        };

        var candidates = AstHealer.FindCandidates(intent);

        Assert.NotEmpty(candidates);
        var top = candidates[0];
        Assert.Equal("role", top.Strategy);
        Assert.Contains("getByRole", top.Selector);
        Assert.Contains("Confirm booking", top.Selector);
        Assert.True(top.CombinedConfidence >= 0.80);
    }

    [Fact]
    public void AstHealer_ProposePatch_GeneratesSyntacticallySoundDiff()
    {
        var originalSpec = """
        import { test, expect } from '@playwright/test';

        test('guest can checkout', async ({ page }) => {
          await page.goto('http://localhost:3000/cart');
          await page.locator('#legacy-book-btn').click();
          await expect(page).toHaveURL('/confirmation');
        });
        """;

        var intent = new IntentSpec
        {
            OriginalIntent = "Confirm booking",
            FailedLocator = "#legacy-book-btn"
        };

        var candidate = new CandidateLocator
        {
            Selector = "page.getByRole('button', { name: 'Confirm booking' })",
            Strategy = "role",
            MatchScore = 0.95,
            StabilityScore = 0.95,
            Rationale = "Direct semantic role match"
        };

        var patch = AstHealer.ProposePatch(originalSpec, intent, candidate, "tests/checkout.spec.ts");

        Assert.True(patch.IsValidated);
        Assert.Contains("+   await page.getByRole('button', { name: 'Confirm booking' }).click();", patch.UnifiedDiff);
        Assert.Contains("-   await page.locator('#legacy-book-btn').click();", patch.UnifiedDiff);
        Assert.StartsWith("heal/fix-locator-", patch.BranchName);
        Assert.Contains("fix(test): auto-heal locator", patch.PrTitle);
    }
}

