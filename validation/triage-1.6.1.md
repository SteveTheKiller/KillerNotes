# KillerNotes 1.6.1 issue work

Date: October 22, 2026. Branch: main. Starting commit: d5f4f2d.

## Scope and authorization

Steve requested starting work on all issues discussed after checking the latest GitHub comments:
KillerNotes #18 (search navigation and intermittent freezes), #29 (RTF and other file extensions),
#22 (screen reader controls), and #28 (typing near horizontal rules).
The preceding request also authorized opening 1.6.1, updating the project version, and repairing
missing grain on the locked screen. Local investigation, bounded implementation, tests and commits
are authorized. Posting replies, closing issues, pushing and releasing are separate actions.

The GitHub connector successfully read all four issue bodies and their full comment collections.
The requested repository is public. No other repository or private issue collection was requested.
The newest reporter replies are linked from [#18](https://github.com/SteveTheKiller/KillerNotes/issues/18#issuecomment-6336705714)
and [#29](https://github.com/SteveTheKiller/KillerNotes/issues/29#issuecomment-6338654859).

## Search (#18)

Two confirmed causes: F3 focused the search box instead of navigating global matches, and
StepFind no longer called the existing next-note helper at the end of a note. Global search
also needs its own active match index, preserved across editor refreshes and cleared when the
query or document changes. The command now uses the active search, navigates forward/backward,
wraps, and skips title-only, tag-only and unreadable candidates. Navigation saves before changing
notes and retains the existing failed-save guard.

Regression seams: the shell's navigation command with a real RichTextBox, the candidate lookup
against a temporary real NoteStore, and existing cross-note Find tests. A refresh test failed
when the global index was reset and passed after preserving its text position.

Intermittent-freeze investigation predictions:

1. Repeated document scans would make an edit burst progressively slower. Existing coalesced
   refresh tests exercise 4,000-paragraph documents and retain the current match.
2. Loading every candidate would scale with unrelated note contents. Global candidate navigation
   first checks stored search text, then loads only candidates with body matches to verify them.
3. Highlight geometry or scrolling would stall while navigating widely separated matches.
   The rendered stress fixture navigates all 60 matches spread over 4,000 paragraphs.

The stress fixture completed those 60 navigation and dispatcher cycles in 915 ms on this machine.
That does not reproduce or resolve the reporter's intermittent Windows 10 freeze. It still needs
the query, approximate note sizes, trigger sequence, and preferably a redacted failing note or trace.
No speculative performance change or freeze-fix claim is made.

## External files (#29)

The reporter confirmed TXT opening works on Windows 10. RTF, LOG, HTML and HTM now join the
existing Markdown and TXT Open with registration and startup/single-instance routing. The shell
uses its existing RTF, text and HTML importers instead of treating these files as shared notes.
RTF retains formatting; HTML imports as source, consistent with Ctrl+O. Default file associations
remain untouched. The registration test failed for the added extensions before the change.
An import round trip verifies bold text and a non-ASCII character in stored RTF content.

## Screen readers (#22)

No new reporter reply identifies a remaining control. Existing accessibility checks exercise
control names, automation peers, keyboard alternatives and overlay behavior. No running JAWS or
NVDA process was found during this task. These checks do not establish screen reader acceptance;
the reporter's JAWS/NVDA retest remains outstanding. No speculative control changes were made.

## Horizontal rules (#28)

No new failure report follows the shipped caret guard. A focused test attaches the actual guard
to a real RichTextBox, moves its caret onto a 2pt rule from each arrow direction and the default
mouse direction, then types. All five cases land in the adjacent 13pt paragraph and leave the
rule empty. No additional product change was needed. Reporter confirmation remains outstanding.

## Locked screen and version

The version, assembly version and file version now target 1.6.1. ReleaseDate remains 2026-10-21
and the new changelog heading is Unreleased.

The lock screen replaced the hidden editor with a flat Grid that had no grain layer. It now uses
the existing GrainTileBrush and FilmGrainVisibility at the editor's existing strength. Before the
fix, rendered lock-screen checks failed with unchanged pixels when toggling grain. Afterward,
Dark, Light, Black and Malaise render different pixels with grain on and exactly the no-texture
background with it off. Malaise's background gradient is preserved.

This is the app-specific locked-session panel, not a shared theme or control change. KillerUI has
no corresponding lock screen. Shared theme tokens, the grain generator and other apps are unchanged.
The tests render the lock panel extracted from current markup; they do not replace interactive
verification of the complete installed app or Windows 10 testing.

## Verification checkpoint

Focused navigation, RTF, rendered grain, accessibility and horizontal-rule checks passed.
The complete Release suite passed after the final scrolling adjustment: 762 passed, 3 skipped,
765 total, in 2 minutes 4 seconds. A preceding full run also passed. The three skips are existing
test conditions, not newly skipped checks.

Reproduce with:

```powershell
dotnet test KillerNotes.Tests\KillerNotes.Tests.csproj -c Release --no-restore --verbosity quiet
```

Local implementation commits: 361e7ff (version and locked-screen grain), 0ecb84a (search),
and 0b809f3 (external file formats). The final suite built the app and test project successfully.
Whitespace checks passed and the changed content contains no prohibited dash characters.
No GitHub comments, labels or issue states were changed. Nothing was pushed or released.

Remaining acceptance work: reproduce the intermittent freeze using the reporter's failing case;
retest the full app on Windows 10, including keyboard navigation and cold/warm Explorer opening;
and obtain the JAWS/NVDA and horizontal-rule reporter confirmations. Those remain open rather
than being treated as completed by automated checks.
