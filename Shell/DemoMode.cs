using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // Screenshot / demo mode. Launch with `KillerNotes.exe --demo` (or /demo). A scratch
    // database (demo-notes.db) is recreated on every demo launch and filled with the
    // fabricated notes below; the real notes.db is never opened. Only reachable through
    // the launch flag - no button, safe to leave in a shipped build (KillerScan pattern).
    //
    // Everything here is invented. Client names are fictional and every address uses the
    // TEST-NET documentation ranges, so nothing matches any real environment.
    public partial class MainWindow
    {
        public static bool DemoMode;
        public static bool DemoFresh = true;   // false = stale demo db survived (locked); don't re-seed it

        // Demo tags: MSP-flavored named tags (order sets the Ctrl+1..6 slots) that replace
        // the auto-seeded color-named defaults, so screenshots show real categories.
        private static readonly (string Name, string Color)[] DemoTags =
        [
            ("On-site",           "#50AEE8"),
            ("Urgent",            "#DD504B"),
            ("Follow-up",         "#E8962C"),
            ("Network",           "#B982E3"),
            ("Reference",         "#1EA54C"),
            ("Waiting on vendor", "#E8D44B"),
        ];

        private void GenerateDemoNotes()
        {
            if (!NoteStore.IsOpen) return;
            var now = DateTime.Now;
            long showcase = -1;

            void Add(string title, double daysAgo, FlowDocument doc, bool feature = false, string? tags = null, string? group = null, string? titleColor = null)
            {
                long id = CreateNoteFromDocument(title, doc);   // ImportExport.cs
                // Demo sketches: persist any editable SketchPad payload an image in this doc carries,
                // keyed by the image's ordinal in document order - the same link OpenNote uses to
                // re-attach strokes on load (Editor.cs), so a printed demo sketch reopens editable.
                var sketchByOrd = new Dictionary<int, byte[]>();
                int sOrd = 0;
                foreach (var im in EnumerateImages(doc.Blocks))
                {
                    if (Sketch.TryGetData(im, out var payload)) sketchByOrd[sOrd] = payload;
                    sOrd++;
                }
                if (sketchByOrd.Count > 0) NoteStore.SaveSketches(id, sketchByOrd);
                // DemoDocMono marks its documents so the code-oriented demo notes open with
                // highlighting on. The Tag is an in-memory hint only - it is not serialized
                // with the blob - so the flag is written to the note's metadata here.
                if (Equals(doc.Tag, SyntaxTag)) NoteStore.SetSyntaxHighlight(id, true);
                // Demo notes never pass through SaveCurrentNote, which is where a real note's
                // wikilinks get indexed - so index them here or the graph and the backlinks strip
                // open empty on the one database built to show them off.
                NoteStore.SetLinks(id, WikiLinks.Parse(
                    new TextRange(doc.ContentStart, doc.ContentEnd).Text));
                var created = now.AddDays(-daysAgo);
                var modified = created.AddHours(2 + (daysAgo % 5) * 7);
                if (modified > now) modified = now.AddMinutes(-14);
                NoteStore.SetTimestamps(id, created, modified);
                if (tags != null) NoteStore.SetNoteTags(id, tags);
                if (group != null) NoteStore.SetNoteGroup(id, group);
                if (titleColor != null) NoteStore.SetTitleColor(id, titleColor);
                if (feature) showcase = id;
            }

            // Swap the color-named defaults for the MSP-flavored set, then tag the notes.
            foreach (var t in NoteStore.ListTags()) NoteStore.DeleteTag(t.Name);
            foreach (var t in DemoTags) NoteStore.AddTag(t.Name, t.Color);

            // Family accent palette, reused across group colors + a few note titles.
            const string BLUE = "#50AEE8", RED = "#DD504B", ORANGE = "#E8962C", PURPLE = "#B982E3",
                         GREEN = "#1EA54C", YELLOW = "#E8D44B", TEAL = "#2BB6A3", PINK = "#E86FA6",
                         INDIGO = "#6A6AE3", SLATE = "#7A8CA3";

            // Build a nested group PATH from its parts ("A","B" => A<sep>B).
            string P(params string[] parts)
            {
                string p = "";
                foreach (var s in parts) p = NoteStore.GroupPath(p, s);
                return p;
            }
            // Create a (sub)group at parts and color it. Parents are created first (calls run in
            // pre-order), which also sets the top-to-bottom sidebar order.
            void G(string color, params string[] parts)
            {
                string path = P(parts);
                string parent = NoteStore.GroupParentOf(path);   // net48 has no array-range slicing
                NoteStore.AddGroup(path, parent, atTop: false);   // pre-order build => keep top-to-bottom order
                NoteStore.SetGroupColor(path, color);
            }

            // A deep, colorful tree so the demo shows groups, nested subgroups (up to three
            // levels), and per-group colors. A few notes stay ungrouped as the loose tail (#8).
            G(BLUE,   "Client sites");
            G(TEAL,   "Client sites", "Northwind Dental");
            G(RED,    "Client sites", "Meadowbrook Vet");
            G(INDIGO, "Client sites", "Oakfield Law");
            G(ORANGE, "Client sites", "Oakfield Law", "Phase 2");
            G(PURPLE, "Bench reference");
            G(GREEN,  "Bench reference", "PowerShell");
            G(ORANGE, "Bench reference", "Networking");
            G(BLUE,   "Bench reference", "Networking", "VLAN cheatsheets");
            G(YELLOW, "Bench reference", "Hardware");
            G(TEAL,   "Bench reference", "Scripts");
            G(PINK,   "Projects");
            G(RED,    "Projects", "Firewall refresh");
            G(BLUE,   "Projects", "Wi-Fi survey");
            G(SLATE,  "Admin");

            // ---- Client sites ----
            Add("Northwind Dental - site visit", 38, DemoSiteVisit(), tags: "On-site, Reference", group: P("Client sites", "Northwind Dental"));
            Add("Imaging server quirk", 35, DemoDoc("The bench imaging box drops its second NIC after a reboot. Disable and re-enable it, or just leave NIC1 patched.",
                "PXE only works on NIC1", "Static 192.0.2.40 /24 on the imaging VLAN", "Amber light is a bad LED, not the PSU"),
                tags: "Reference", group: P("Client sites", "Northwind Dental"), titleColor: TEAL);
            Add("After-hours contacts", 30, DemoDoc("Escalation for the main office, in order:",
                "Office manager - has the alarm code", "Practice owner - text first, never call after 21:00", "Alarm company passphrase is on the work order"),
                tags: "Reference", group: P("Client sites", "Northwind Dental"));

            Add("Firewall swap - Meadowbrook Vet", 31, DemoFirewallSwap(), feature: true, tags: "On-site, Network", group: P("Client sites", "Meadowbrook Vet"), titleColor: RED);
            Add("MDF rack elevation", 28, DemoRackSketch(), tags: "On-site, Reference", group: P("Client sites", "Meadowbrook Vet"), titleColor: RED);
            Add("Kennel cams offline", 26, DemoDoc("Four PoE cameras in the kennel keep dropping. Suspect the cheap unmanaged switch back there.",
                "Swap in the spare PoE+ switch from the van", "Camera VLAN 40, DHCP off, static .50-.70", "If they still drop it is the long run near the compressor"),
                tags: "Urgent, Follow-up", group: P("Client sites", "Meadowbrook Vet"));
            Add("Kennel cams - photos", 25, DemoCamStation(), tags: "On-site, Urgent", group: P("Client sites", "Meadowbrook Vet"));
            Add("Printer mapping", 22, DemoDoc("Shared printers by room, for the deploy script:",
                "Front desk - HP M428 (192.0.2.61)", "Lab - Brother HL-L2350 (192.0.2.62)", "Back office Lexmark - do not map, they want it gone"),
                tags: "Reference", group: P("Client sites", "Meadowbrook Vet"));

            Add("Switch port map - Oakfield Law", 20, DemoPortMap(), tags: "Network, Reference", group: P("Client sites", "Oakfield Law"));
            Add("VPN user list", 18, DemoDoc("Who has client VPN and why. Review quarterly.",
                "3 partners - always on", "2 paralegals - remote days only", "1 vendor account - disable when the case closes"),
                tags: "Reference", group: P("Client sites", "Oakfield Law"));

            Add("Phase 2 - cabling scope", 15, DemoDoc("Second-floor buildout. Rough count before the quote:",
                "14 new drops, all Cat6", "2 WAPs at the hallway ends", "Home-run to the second-floor IDF, not the MDF"),
                tags: "On-site", group: P("Client sites", "Oakfield Law", "Phase 2"));
            Add("Phase 2 - cutover runbook", 13, DemoDoc("Order of operations for the cutover weekend:",
                "Label and test every new drop Friday PM", "Move users desk by desk Saturday", "Old IDF stays live one week as rollback"),
                tags: "Follow-up", group: P("Client sites", "Oakfield Law", "Phase 2"), titleColor: ORANGE);

            // ---- Bench reference ----
            Add("PowerShell one-liners", 27, DemoPowerShell(), tags: "Reference", group: P("Bench reference", "PowerShell"));
            Add("Bulk AD password reset", 24, DemoDocMono("Force a reset at next logon for a whole OU:",
                "Get-ADUser -Filter * -SearchBase 'OU=Staff,DC=corp,DC=local' | Set-ADUser -ChangePasswordAtLogon $true",
                "Skip the service accounts - they live in OU=Service", "Hand out the temp passwords out of band"),
                tags: "Reference", group: P("Bench reference", "PowerShell"));
            Add("Export mailbox sizes", 19, DemoDocMono("Quick capacity check before a migration:",
                "Get-MailboxStatistics -Server EX01 | Sort TotalItemSize -Desc | Select DisplayName,TotalItemSize"),
                tags: "Reference", group: P("Bench reference", "PowerShell"));

            Add("Subnet quick math", 21, DemoSubnet(), tags: "Reference", group: P("Bench reference", "Networking"));
            Add("DNS troubleshooting order", 17, DemoDoc("When name resolution is flaky, work it in this order:",
                "flushdns, then nslookup against the server directly", "Check the forwarders on the DNS server, not just the client",
                "Confirm the client points at the internal DNS, not the router", "Only then suspect the record itself"),
                tags: "Reference", group: P("Bench reference", "Networking"));

            Add("VLAN numbering standard", 14, DemoVlan(), tags: "Reference", group: P("Bench reference", "Networking", "VLAN cheatsheets"), titleColor: BLUE);
            Add("Trunk config snippets", 11, DemoDocMono("The uplink trunk I paste on every access switch:",
                "switchport mode trunk ; switchport trunk allowed vlan 10,20,30,40,99", "Native VLAN 1, unused everywhere"),
                tags: "Reference", group: P("Bench reference", "Networking", "VLAN cheatsheets"));

            Add("UPS runtimes", 16, DemoUps(), tags: "Reference, Follow-up", group: P("Bench reference", "Hardware"));
            Add("Parts drawer inventory", 5, DemoParts(), tags: "Reference", group: P("Bench reference", "Hardware"));
            Add("Bench photos", 7, DemoBenchPhotos(), tags: "Reference", group: P("Bench reference", "Hardware"), titleColor: YELLOW);
            Add("Syntax highlighting - all languages", 23, DemoSyntaxShowcase(), tags: "Reference", group: P("Bench reference", "Scripts"), titleColor: TEAL);
            Add("Patch baseline playbook", 18, DemoAnsibleYaml(), tags: "Reference", group: P("Bench reference", "Scripts"));
            Add("New starter provisioning", 15, DemoProvisionScript(), tags: "Reference", group: P("Bench reference", "Scripts"), titleColor: GREEN);

            Add("Drive shucking notes", 9, DemoDoc("Cheap external drives for the backup rotation:",
                "Tape over the 3.3V pin or the drive will not spin in the NAS", "8TB+ white-labels are usually CMR, but test",
                "Log the serial before shucking - warranty voids"),
                tags: "Reference", group: P("Bench reference", "Hardware"));

            // ---- Projects ----
            Add("Firewall refresh - vendor quotes", 12, DemoDoc("Comparing the two firewall vendors for the fleet refresh. Waiting on the second quote.",
                "Vendor A - cheaper box, pricier licensing", "Vendor B - better throughput, 3-year bundle", "Both cover the VLANs and the site-to-site we need"),
                tags: "Waiting on vendor", group: P("Projects", "Firewall refresh"));
            Add("Firewall refresh - migration plan", 10, DemoDoc("One site per weekend, lowest-risk first:",
                "Start with the single-VPN sites", "Multi-peer sites last, once the runbook is solid", "Keep each old unit racked a week as rollback"),
                tags: "Follow-up", group: P("Projects", "Firewall refresh"), titleColor: RED);

            Add("Wi-Fi survey - AP placement", 8, DemoDoc("Walk-through notes for the warehouse survey:",
                "Dead spot at the far loading dock - needs its own AP", "Office side is fine on 2 APs", "Metal racking kills 5GHz down the aisles"),
                tags: "On-site", group: P("Projects", "Wi-Fi survey"));
            Add("Wi-Fi survey - channel plan", 7, DemoDoc("Non-overlapping channel plan after the survey:",
                "2.4GHz - 1, 6, 11 only, never auto", "5GHz - let the controller pick but cap TX power", "Neighboring tenant sits on 6, keep our high-density APs off it"),
                tags: "Reference", group: P("Projects", "Wi-Fi survey"));
            Add("Wi-Fi survey - coverage sketch", 6, DemoWifiSketch(), tags: "On-site, Follow-up", group: P("Projects", "Wi-Fi survey"), titleColor: BLUE);

            // ---- Admin (notes filed directly in the group, no subgroups) ----
            Add("New tech onboarding", 12, DemoOnboarding(), tags: "Reference", group: P("Admin"));
            Add("On-call rotation", 6, DemoOnCall(), tags: "Reference", group: P("Admin"));
            Add("Expense receipts - reminder", 4, DemoDoc("Submit receipts by the last business day or they roll to next month.",
                "Photograph the receipt at the counter, do not save it for later", "Mileage log is in the shared drive", "Personal-card parts need the PO number in the memo"),
                tags: "Follow-up", group: P("Admin"), titleColor: SLATE);

            // ---- Preview detection, three ways (1.1.7, #14) ----
            // Click down the list to see the difference: the runbook offers a Preview button, the
            // banner detects as HTML, and the checklist offers nothing - it has dash bullets and
            // numbered steps but no strong markdown signal, which is exactly the case that used to
            // produce a spurious Preview button before the detector was tightened.
            Add("Failover runbook", 11, DemoMarkdownRunbook(), tags: "Reference",
                group: P("Admin"), titleColor: GREEN);
            Add("Maintenance banner (HTML)", 5, DemoHtmlSnippet(), tags: "Reference",
                group: P("Admin"));
            Add("Meadowbrook cutover checklist", 3, DemoPlainChecklist(), tags: "On-site",
                group: P("Client sites", "Meadowbrook Vet"));

            // ══════════════ SECOND BRAIN ══════════════
            //
            // A real link graph, not a handful of token links. This is the half of demo mode that
            // exercises wikilinks, the backlinks strip and the graph window, and it is built the
            // way a working notebook actually grows: a few INDEX notes that gather a subject, a
            // layer of concept notes that reference each other sideways, and links back into the
            // client notes above so those get backlinks without their own text being rewritten.
            //
            // Deliberate shapes to test against:
            //   - Hubs. "Networking index" and "Escalation" are linked from many notes, so they
            //     draw as the big nodes and prove the degree sizing works.
            //   - A CHAIN, VLAN plan -> trunking -> port map, so the graph has depth and not just
            //     a star around one hub.
            //   - A CYCLE, imaging <-> PXE <-> driver packs, which a naive layout tangles.
            //   - GHOSTS: several notes link "[[Cable standards]]" and "[[Client onboarding]]",
            //     which are deliberately never created, so the dashed unwritten-note nodes appear.
            //   - Links INTO the client notes above, which is what puts entries in their
            //     backlinks strip.
            G(INDIGO, "Second brain");
            G(PINK,   "Second brain", "Concepts");

            Add("Networking index", 44, DemoDoc("Everything network, gathered. Start here.",
                "Addressing and segments: [[VLAN plan]]", "Uplinks: [[Trunking]] and [[Cable standards]]",
                "Wireless: [[Wi-Fi channel plan]], [[AP placement]]",
                "Live examples: [[Switch port map - Oakfield Law]], [[Firewall swap - Meadowbrook Vet]]"),
                tags: "Reference, Network", group: P("Second brain"), titleColor: BLUE);

            Add("VLAN plan", 43, DemoDoc("The standard numbering used on every site so nothing has to be remembered twice.",
                "10 data, 20 voice, 30 guest, 40 cameras, 50 management",
                "Guest is internet-only, enforced on the firewall - see [[Firewall swap - Meadowbrook Vet]]",
                "Cameras never route, see [[Kennel cams offline]] for why that matters",
                "Uplinks carry all of them: [[Trunking]]"),
                tags: "Network, Reference", group: P("Second brain", "Concepts"), titleColor: BLUE);

            Add("Trunking", 42, DemoDoc("Rules for uplinks between switches. Gets this wrong once per site if you let it.",
                "Tag everything except management, which stays native",
                "Both ends must agree - a mismatch shows as one VLAN working and the rest silent",
                "Numbering comes from [[VLAN plan]]",
                "Worked example: [[Switch port map - Oakfield Law]]",
                "Physical side: [[Cable standards]]"),
                tags: "Network", group: P("Second brain", "Concepts"), titleColor: BLUE);

            Add("Wi-Fi channel plan", 41, DemoDoc("2.4 is a lost cause, 5 is where the work is.",
                "1, 6, 11 only on 2.4, and turn the power down",
                "5 GHz: 20 MHz wide in an office, 40 only if the site is empty around it",
                "Placement decides more than channels do: [[AP placement]]",
                "Part of [[Networking index]]"),
                tags: "Network, Reference", group: P("Second brain", "Concepts"));

            Add("AP placement", 40, DemoDoc("Where the access points go, which nobody gets right from a floor plan alone.",
                "Ceiling, centre of the space, never above a rack",
                "One per 1500 sq ft of open office, closer where there are hard walls",
                "Survey before quoting: the second-floor job is [[Phase 2 - cabling scope]]",
                "Channels: [[Wi-Fi channel plan]]"),
                tags: "Network", group: P("Second brain", "Concepts"));

            Add("Imaging", 39, DemoDoc("Bench imaging, end to end. The three notes here are circular on purpose - you cannot understand one without the others.",
                "Boot: [[PXE boot]]", "Hardware support: [[Driver packs]]",
                "The machine that does it: [[Imaging server quirk]]",
                "Naming and joining is part of [[Client onboarding]]"),
                tags: "Reference", group: P("Second brain"), titleColor: TEAL);

            Add("PXE boot", 38, DemoDoc("Network boot, and the four things that break it.",
                "DHCP scope option 66/67, or a proxy if the firewall runs DHCP",
                "Legacy and UEFI need different boot files - most failures are this",
                "Only NIC1 on the bench box: [[Imaging server quirk]]",
                "Once it boots you still need [[Driver packs]]", "Back to [[Imaging]]"),
                tags: "Reference", group: P("Second brain", "Concepts"), titleColor: TEAL);

            Add("Driver packs", 37, DemoDoc("Vendor driver bundles, injected at deploy time.",
                "One pack per model per OS build, and they go stale quietly",
                "A missing NIC driver looks exactly like [[PXE boot]] failing, which costs an hour every time",
                "Storage driver missing = no disk found at setup", "Back to [[Imaging]]"),
                tags: "Reference", group: P("Second brain", "Concepts"), titleColor: TEAL);

            Add("Escalation", 36, DemoDoc("Who to wake, and when. Linked from most of the on-site notes.",
                "Anything after 21:00 goes to the on-call phone, not to a person",
                "Client contacts live with the client: [[After-hours contacts]]",
                "Outage comms template is in [[Maintenance banner (HTML)]]",
                "If it is a failover, follow [[Failover runbook]] first and escalate after"),
                tags: "Reference, Urgent", group: P("Second brain"), titleColor: RED);

            Add("Site visit checklist", 35, DemoDoc("What goes in the bag and what gets written down. Applies to every site.",
                "Photograph the rack before touching it - like [[MDF rack elevation]]",
                "Record every port you move: [[Switch port map - Oakfield Law]] is the format",
                "Printers get mapped from [[Printer mapping]], never guessed",
                "Leaving: update the ticket, then [[Escalation]] if anything is unfinished",
                "New client? [[Client onboarding]]"),
                tags: "On-site, Reference", group: P("Second brain"), titleColor: ORANGE);

            Add("Cutover", 34, DemoDoc("Moving production from old to new without a rollback you cannot take.",
                "Never cut over without the old path still live - [[Phase 2 - cutover runbook]]",
                "Test plan written BEFORE the window, not during",
                "Real example with timings: [[Meadowbrook cutover checklist]]",
                "Failure path: [[Failover runbook]] then [[Escalation]]"),
                tags: "Reference, Follow-up", group: P("Second brain"), titleColor: ORANGE);

            Add("VPN access", 33, DemoDoc("Who gets remote access and how it is reviewed.",
                "Named accounts only, never shared - the audit is [[VPN user list]]",
                "Vendor accounts are time-boxed and disabled at case close",
                "Split tunnel off for anyone touching client data",
                "Onboarding and offboarding both live in [[Client onboarding]]"),
                tags: "Reference", group: P("Second brain"), titleColor: GREEN);

            Add("Backups", 32, DemoDoc("The thing every other note quietly depends on.",
                "3-2-1, and the offsite copy is the one that gets forgotten",
                "A backup nobody has restored from is a hypothesis, not a backup",
                "Restore test before any [[Cutover]]",
                "Storage sizing feeds the NAS quote in [[Callback list]]"),
                tags: "Reference, Urgent", group: P("Second brain"), titleColor: RED);

            // ══════════════ UNLINKED MENTIONS ══════════════
            //
            // Day notes that NAME other notes in ordinary prose and never link them. That is the
            // whole point of the "Mentioned in" strip: the connection is already in the text and
            // nobody has made it yet, so opening any of the notes named below shows these sitting
            // underneath it waiting to be linked with one click.
            //
            // Every capitalised run below is an EXACT title of a note created above - "Escalation",
            // "Backups", "Cutover", "VLAN plan", "Trunking", "Imaging", "PXE boot", "Driver packs",
            // "Printer mapping", "AP placement", "Site visit checklist", "Failover runbook". They
            // are deliberately written as bare words, never as [[links]], because a note that
            // links a title is excluded from that title's mentions - which is exactly the contrast
            // this section exists to show.
            G(SLATE, "Day notes");

            Add("Tuesday", 6, DemoDoc("Half the day on the vet site, half on the bench.",
                "Ran the Site visit checklist before touching anything, photos first",
                "Kennel cams offline again, swapped the PoE switch, watching it",
                "Bench: two machines through Imaging, one failed at PXE boot until I refreshed the driver packs",
                "Escalation was not needed, nobody was down"),
                tags: "On-site", group: P("Day notes"));

            Add("Wednesday", 5, DemoDoc("Quiet. Mostly paperwork and one cutover prep call.",
                "Walked the client through Cutover timing, they want a Friday window",
                "Confirmed Backups ran clean for three nights before we touch anything",
                "Printer mapping needs updating, the Lexmark is finally gone",
                "Asked about the VLAN plan for the new floor, waiting on the cabling quote"),
                tags: "Follow-up", group: P("Day notes"));

            Add("Thursday", 4, DemoDoc("Wi-Fi day.",
                "Redid AP placement on the second floor, two APs moved off the racks",
                "Channel plan holds, no overlap with the neighbours now",
                "Trunking on the new switch was tagged wrong, one VLAN silent until I fixed it",
                "Left the Failover runbook printed in the MDF for the on-call tech"),
                tags: "On-site, Network", group: P("Day notes"));

            Add("Parking lot", 3, DemoDoc("Things I keep meaning to write up properly.",
                "The Imaging server quirk deserves a real note, I explain it monthly",
                "Someone should document VPN access before the next audit",
                "Escalation after 21:00 is still ambiguous for the vet site",
                "Cable standards, still not written, still referenced constantly"),
                tags: "Follow-up", group: P("Day notes"), titleColor: YELLOW);

            // ---- Ungrouped tail ----
            Add("RMM agent cleanup", 8, DemoRmm(), tags: "Follow-up", titleColor: ORANGE);
            Add("Callback list", 1, DemoDoc("Loose ends to chase tomorrow:",
                "NAS quote - they want the 4-bay after all", "Ticket #4183 still waiting on the ISP", "Return the loaner laptop to the vet office"),
                tags: "Follow-up");
            Add("Scratch", 0.05, DemoScratch(), tags: "Urgent, Waiting on vendor");

            SearchBox.Text = "";
            RefreshList();
            if (showcase >= 0)
            {
                OpenNote(showcase);
                _syncingSelection = true;
                NotesList.SelectedItem = _notes.Find(n => n.Id == showcase);
                _syncingSelection = false;
            }
            StatusText.Text = $"{_notes.Count} notes";
        }

        // ---- Small builders (concrete brushes on purpose: XamlPackage blobs cannot
        //      keep theme-reactive references, same rule as the horizontal-rule insert) ----

        private static Paragraph DemoP(string text, bool bold = false, string? color = null)
        {
            var run = new Run(text);
            if (bold) run.FontWeight = FontWeights.Bold;
            if (color != null) run.Foreground =
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            return new Paragraph(run);
        }

        private static Paragraph DemoMono(string text)
        {
            var p = new Paragraph(new Run(text)) { FontFamily = new FontFamily("Consolas") };
            return p;
        }

        // Compact builders for the many short demo notes: an intro paragraph plus an optional
        // bullet list (DemoDoc) or a mono command line between them (DemoDocMono).
        private static FlowDocument DemoDoc(string intro, params string[] bullets)
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP(intro));
            if (bullets.Length > 0) d.Blocks.Add(DemoList(bullets));
            return d;
        }

        private static FlowDocument DemoDocMono(string intro, string mono, params string[] bullets)
        {
            // Code-oriented demo notes should showcase the same per-note toggle state a user
            // gets after pressing </>. The marker is read back by Add(), which writes the flag
            // into the note's metadata; it does not travel in the document blob.
            var d = new FlowDocument { Tag = SyntaxTag };
            d.Blocks.Add(DemoP(intro));
            d.Blocks.Add(DemoMono(mono));
            if (bullets.Length > 0) d.Blocks.Add(DemoList(bullets));
            return d;
        }

        private static List DemoList(params string[] items)
        {
            var list = new List { MarkerStyle = TextMarkerStyle.Disc };
            foreach (var i in items) list.ListItems.Add(new ListItem(new Paragraph(new Run(i))));
            return list;
        }

        private static Paragraph DemoRule() => new()
        {
            FontSize = 2,
            Margin = new Thickness(0, 8, 0, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x5a, 0x5a, 0x5a)),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        private static Table DemoTable(string[] header, params string[][] rows)
        {
            var border = new SolidColorBrush(Color.FromRgb(0x5a, 0x5a, 0x5a));
            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 6), BorderBrush = border, BorderThickness = new Thickness(1, 1, 0, 0) };
            for (int c = 0; c < header.Length; c++) table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            var head = new TableRow();
            foreach (var h in header)
            {
                var run = new Run(h) { FontWeight = FontWeights.Bold };
                head.Cells.Add(new TableCell(new Paragraph(run))
                { BorderBrush = border, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(6, 3, 6, 3) });
            }
            group.Rows.Add(head);
            foreach (var row in rows)
            {
                var tr = new TableRow();
                foreach (var cell in row)
                    tr.Cells.Add(new TableCell(new Paragraph(new Run(cell)))
                    { BorderBrush = border, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(6, 3, 6, 3) });
                group.Rows.Add(tr);
            }
            table.RowGroups.Add(group);
            return table;
        }

        // ---- The notes ----

        private static FlowDocument DemoSiteVisit()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Quarterly maintenance, main office. On-site window 8:00-12:00.", bold: false));
            d.Blocks.Add(DemoList(
                "Replace UPS batteries in the MDF (2x RBC115, in the van)",
                "Firmware on both switches - approved by office manager",
                "Check backup job history - NAS reported 2 warnings last week",
                "Label the new drops in suite 210",
                "Grab a photo of the patch panel BEFORE touching anything"));
            d.Blocks.Add(DemoRule());
            d.Blocks.Add(DemoP("Gate code is on the work order. Park behind the building.", color: "#c9a227"));
            return d;
        }

        private static FlowDocument DemoFirewallSwap()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Cutover scheduled Friday 17:30. Old unit stays racked for one week as rollback.", bold: true));
            d.Blocks.Add(DemoTable(
                ["Setting", "Old unit", "New unit"],
                ["WAN IP", "203.0.113.10 /29", "203.0.113.10 /29"],
                ["LAN GW", "192.0.2.1 /24", "192.0.2.1 /24"],
                ["VPN peers", "3 (see vendor sheet)", "re-key all 3"],
                ["DNS fwd", "198.51.100.53", "198.51.100.53"],
                ["Mgmt access", "LAN only", "LAN + mgmt VLAN 99"]));
            d.Blocks.Add(DemoP("Port-forward list exported and attached to the ticket. Test plan:"));
            d.Blocks.Add(DemoList(
                "VPN up from all 3 peers",
                "Phones re-register (SIP ALG stays OFF)",
                "Guest Wi-Fi isolated from LAN",
                "Speed test before/after"));
            return d;
        }

        private static FlowDocument DemoPowerShell()
        {
            var d = new FlowDocument { Tag = SyntaxTag };
            d.Blocks.Add(DemoP("The ones I always forget:"));
            d.Blocks.Add(DemoMono("Get-WinEvent -FilterHashtable @{LogName='System';Level=2} -MaxEvents 25"));
            d.Blocks.Add(DemoMono("Test-NetConnection 192.0.2.20 -Port 3389"));
            d.Blocks.Add(DemoMono("Get-Volume; Get-Disk | Sort Number"));
            d.Blocks.Add(DemoMono("gpupdate /force; gpresult /h C:\\temp\\gp.html"));
            d.Blocks.Add(DemoRule());
            d.Blocks.Add(DemoP("All PS 5.1-safe. Keep them one line for LiveConnect.", color: "#3f9b56"));
            return d;
        }

        private static FlowDocument DemoPortMap()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoTable(
                ["Port", "VLAN", "Goes to"],
                ["1-8", "10", "Workstations, front office"],
                ["9-12", "10", "Workstations, paralegals"],
                ["13-16", "20", "Phones"],
                ["17-18", "30", "Printers"],
                ["19-22", "10", "Conference rooms"],
                ["23", "99", "AP uplink (trunk)"],
                ["24", "trunk", "Uplink to firewall"]));
            d.Blocks.Add(DemoP("Spare drops in the ceiling above suite B - unterminated."));
            return d;
        }

        private static FlowDocument DemoUps()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Measured on battery with normal load, this quarter:"));
            d.Blocks.Add(DemoTable(
                ["Location", "Model", "Runtime"],
                ["MDF", "1500VA rack", "22 min"],
                ["Front desk", "650VA tower", "9 min"],
                ["Server closet", "3000VA rack", "41 min"]));
            d.Blocks.Add(DemoRule());
            d.Blocks.Add(DemoP("Front desk unit beeps under load - batteries due next visit.", bold: true, color: "#c94f4f"));
            return d;
        }

        private static FlowDocument DemoOnboarding()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Day-one checklist for a new bench tech:"));
            d.Blocks.Add(DemoList(
                "RMM console account + MFA",
                "PSA / ticketing login, assign to triage queue",
                "Bench image USB (kept in the top drawer, re-image monthly)",
                "Label maker tape - we standardize on 12mm",
                "Read the escalation matrix BEFORE the first after-hours call"));
            d.Blocks.Add(DemoP("Shadow a senior on the first two site visits. No exceptions.", bold: true));
            return d;
        }

        private static FlowDocument DemoRmm()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Offboarded client still had 14 agents checking in. Cleanup order matters:"));
            d.Blocks.Add(DemoList(
                "Disable alerting for the site FIRST (or the queue floods)",
                "Uninstall via the console job, verify service is gone",
                "Remove the site from patching policy",
                "Archive the site, do not delete - audit trail stays"));
            d.Blocks.Add(DemoP("Leftover agents show as stale after 30 days - recheck next month."));
            return d;
        }

        private static FlowDocument DemoParts()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoTable(
                ["Part", "Qty", "Reorder at"],
                ["Cat6 patch 1m", "18", "10"],
                ["Cat6 patch 3m", "7", "5"],
                ["SFP+ DAC 3m", "4", "2"],
                ["RJ45 ends (bag)", "2", "1"],
                ["PSU tester", "1", "-"]));
            return d;
        }

        private static FlowDocument DemoSubnet()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("The mask-to-hosts table I never keep in my head:"));
            d.Blocks.Add(DemoTable(
                ["CIDR", "Mask", "Usable hosts"],
                ["/24", "255.255.255.0", "254"],
                ["/25", "255.255.255.128", "126"],
                ["/26", "255.255.255.192", "62"],
                ["/27", "255.255.255.224", "30"],
                ["/28", "255.255.255.240", "14"],
                ["/30", "255.255.255.252", "2"]));
            return d;
        }

        private static FlowDocument DemoVlan()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Standard VLAN numbering we use at every site:"));
            d.Blocks.Add(DemoTable(
                ["VLAN", "Use", "Subnet"],
                ["10", "Workstations", "192.0.2.0 /24"],
                ["20", "Phones", "198.51.100.0 /24"],
                ["30", "Printers", "203.0.113.0 /27"],
                ["40", "Cameras / IoT", "203.0.113.32 /27"],
                ["99", "Management", "203.0.113.240 /28"]));
            d.Blocks.Add(DemoP("Keep cameras and IoT off the workstation VLAN, always.", bold: true, color: "#3f9b56"));
            return d;
        }

        private static FlowDocument DemoOnCall()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Rotation runs Monday to Monday. Swap with whoever, just update the calendar."));
            d.Blocks.Add(DemoTable(
                ["Week", "Primary", "Backup"],
                ["This week", "Me", "Priya"],
                ["Next week", "Dev", "Me"],
                ["Week after", "Priya", "Dev"]));
            d.Blocks.Add(DemoP("After-hours calls go to the on-call phone, not personal numbers."));
            return d;
        }

        private static FlowDocument DemoScratch()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("call back about the NAS quote - they want the 4-bay after all"));
            d.Blocks.Add(DemoP("ticket #4183 waiting on ISP"));
            return d;
        }

        // ---- Demo photos -------------------------------------------------------------------
        //
        // Real photographs, loaded from code\Demo\KillerNotes at seed time rather than shipped as
        // project resources: the demo database is scratch and rebuilt on every --demo launch, so
        // the pictures can be swapped without touching the build. A missing folder or an
        // unreadable file is not an error - the note simply seeds without its picture, so the demo
        // still works on a machine that has never had the folder.

        /// <summary>Where the demo photos live. Sits beside the repo, not inside it.</summary>
        private static string DemoPhotoDir()
        {
            // Walk up from the running exe (bin\Debug\net48) to the code root, then across.
            var dir = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int up = 0; up < 6 && dir != null; up++, dir = dir.Parent)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "Demo", "KillerNotes");
                if (System.IO.Directory.Exists(candidate)) return candidate;
                string sibling = System.IO.Path.Combine(dir.FullName, "..", "Demo", "KillerNotes");
                if (System.IO.Directory.Exists(sibling)) return System.IO.Path.GetFullPath(sibling);
            }
            return "";
        }

        /// <summary>
        /// One demo photo as an in-note Image, or null if it is not there. Decoded and FROZEN with
        /// OnLoad - the same treatment InsertImageAtCaret gives a pasted image, which is what lets
        /// the XamlPackage serializer persist it into the note blob. A file handle is never held
        /// open, so the folder stays swappable while the demo runs.
        /// </summary>
        private static Image? DemoPhoto(string fileName, double maxWidth = 520)
        {
            try
            {
                string dir = DemoPhotoDir();
                if (dir.Length == 0) return null;
                string path = System.IO.Path.Combine(dir, fileName);
                if (!System.IO.File.Exists(path)) return null;

                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();

                var img = new Image { Source = bmp, MaxWidth = maxWidth, Stretch = Stretch.Uniform };
                FixImage(img);   // Fant downscale, same as a pasted photo
                return img;
            }
            catch { return null; }   // unreadable or not an image - seed the note without it
        }

        /// <summary>A photo as its own paragraph, or nothing at all when the file is missing.</summary>
        private static Paragraph? DemoPhotoPara(string fileName, double maxWidth = 520)
        {
            var img = DemoPhoto(fileName, maxWidth);
            if (img == null) return null;
            var p = new Paragraph();
            p.Inlines.Add(new InlineUIContainer(img));
            return p;
        }

        /// <summary>Appends a photo paragraph plus its caption, skipping both if the file is gone.</summary>
        private static void AddPhoto(FlowDocument d, string fileName, string caption, double maxWidth = 520)
        {
            var p = DemoPhotoPara(fileName, maxWidth);
            if (p == null) return;
            d.Blocks.Add(p);
            if (caption.Length > 0) d.Blocks.Add(DemoP(caption, color: "#7A8CA3"));
        }

        private static FlowDocument DemoCamStation()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Camera station in the kennel corridor, photographed before the PoE switch swap.", bold: true));
            AddPhoto(d, "cam_station.png", "As-found. Note the unmanaged switch tucked behind the monitor - that is the one dropping the cameras.");
            d.Blocks.Add(DemoList(
                "Four cameras, all PoE, all on the cheap unmanaged switch",
                "Replace with the spare PoE+ from the van",
                "Camera VLAN 40, static .50-.70, DHCP off"));
            return d;
        }

        private static FlowDocument DemoBenchPhotos()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Reference shots off the bench. Photographed on the phone and pasted straight into the note.", bold: true));
            AddPhoto(d, "old_artist_shot_square.jpg", "Bench overview at the start of the week.");
            d.Blocks.Add(DemoRule());
            AddPhoto(d, "whatanoddthingtodo.png", "Filed under: things the last tech did.");
            d.Blocks.Add(DemoRule());
            AddPhoto(d, "moe.jpeg", "Bench supervisor.", 320);
            return d;
        }

        // ---- Syntax highlighting demos -----------------------------------------------------
        //
        // ApplySyntaxHighlighting detects the language PER PARAGRAPH, not per note, so one note
        // can hold as many languages as it likes and each block lights up in its own palette.
        // That is what the showcase note below is for. It also sets the constraint these demos
        // are written to: a line is sniffed on its own, with no help from the lines around it,
        // so every code line here is one that identifies itself in isolation. The plain-text
        // labels between the blocks deliberately carry NO colon and no tag - a trailing colon
        // would read as YAML and the label would highlight along with the code under it.

        /// <summary>Every language the highlighter knows, one note, one block each.</summary>
        private static FlowDocument DemoSyntaxShowcase()
        {
            var d = new FlowDocument { Tag = SyntaxTag };
            d.Blocks.Add(DemoP("One note, thirteen languages. The highlighter sniffs each paragraph on its own, so a scratch note full of mixed snippets colors itself correctly without a language picker.", bold: true));
            d.Blocks.Add(DemoRule());

            void Block(string label, params string[] lines)
            {
                d.Blocks.Add(DemoP(label, bold: true, color: "#7A8CA3"));
                foreach (var l in lines) d.Blocks.Add(DemoMono(l));
            }

            Block("PowerShell",
                @"$svc = Get-Service -Name Spooler",
                @"if ($svc.Status -ne 'Running') { Start-Service $svc }");
            Block("Python",
                @"import ipaddress",
                @"def sweep(cidr):",
                @"    print(f""{len(hosts)} hosts up on {cidr}"")");
            Block("SQL",
                @"SELECT name, last_seen FROM agents WHERE last_seen < DATEADD(day, -30, GETDATE());");
            Block("Bash",
                @"#!/usr/bin/env bash",
                @"if ping -c1 ""${HOST}"" >/dev/null; then echo up; fi",
                @"for h in ${HOSTS}; do echo ""checking ${h}""; done");
            Block("YAML",
                @"hosts: workstations",
                @"become: true",
                @"- name: Apply security updates");
            Block("JSON",
                @"{ ""site"": ""Northwind"", ""vlan"": 10, ""managed"": true }");
            Block("XAML",
                @"<Border Background=""{DynamicResource PaneBrush}"" CornerRadius=""4"" />");
            Block("HTML",
                @"<div class=""notice""><p>File server offline Sat 06:00-09:00.</p></div>");
            Block("XML",
                @"<agent id=""4183"" site=""Northwind"" lastSeen=""2026-08-01T14:22:00Z"" />");
            Block("CSS",
                @".notice { color: #c94f4f; padding: 8px 12px; }");
            Block("JavaScript",
                @"const stale = agents.filter(a => a.lastSeen < cutoff);");
            Block("TypeScript",
                @"interface Agent { id: string; site: string; lastSeen: Date; }");
            Block("Markdown",
                @"## Failover runbook");

            d.Blocks.Add(DemoRule());
            d.Blocks.Add(DemoP("Toggle the whole note with Ctrl+Shift+E.", color: "#7A8CA3"));
            return d;
        }

        /// <summary>Pure code, no prose. YAML identifies itself line by line, so the whole
        /// playbook colors evenly - the best single showcase of the highlighter.</summary>
        private static FlowDocument DemoAnsibleYaml() => DemoCode(
            @"---",
            @"- name: Monthly patch baseline",
            @"  hosts: workstations",
            @"  become: true",
            @"  vars:",
            @"    reboot_window: ""02:00-04:00""",
            @"    max_reboot_wait: 1800",
            @"  tasks:",
            @"    - name: Refresh the package cache",
            @"      ansible.builtin.apt:",
            @"        update_cache: true",
            @"        cache_valid_time: 3600",
            @"    - name: Apply security updates only",
            @"      ansible.builtin.apt:",
            @"        upgrade: safe",
            @"      register: patch_result",
            @"    - name: Reboot if the kernel changed",
            @"      ansible.builtin.reboot:",
            @"        reboot_timeout: ""{{ max_reboot_wait }}""",
            @"      when: patch_result.changed");

        /// <summary>Pure code, no prose. PS 5.1-safe - nothing here needs PowerShell 7.</summary>
        private static FlowDocument DemoProvisionScript() => DemoCode(
            @"# Creates the AD account, groups and home share for a new starter.",
            @"param(",
            @"    [Parameter(Mandatory)][string]$FirstName,",
            @"    [Parameter(Mandatory)][string]$LastName,",
            @"    [string]$Site = 'Northwind'",
            @")",
            @"",
            @"$ErrorActionPreference = 'Stop'",
            @"$sam  = ($FirstName.Substring(0,1) + $LastName).ToLower()",
            @"$upn  = ""$sam@corp.local""",
            @"$ou   = ""OU=Staff,OU=$Site,DC=corp,DC=local""",
            @"$temp = ConvertTo-SecureString 'Change.Me.2026!' -AsPlainText -Force",
            @"",
            @"if (Get-ADUser -Filter ""SamAccountName -eq '$sam'"") {",
            @"    throw ""User $sam already exists""",
            @"}",
            @"",
            @"New-ADUser -Name ""$FirstName $LastName"" -SamAccountName $sam -UserPrincipalName $upn ` ",
            @"    -Path $ou -AccountPassword $temp -ChangePasswordAtLogon $true -Enabled $true",
            @"",
            @"Add-ADGroupMember -Identity 'RMM-Managed' -Members $sam",
            @"Add-ADGroupMember -Identity ""Site-$Site""  -Members $sam",
            @"",
            @"Write-Host ""Created $upn in $Site"" -ForegroundColor Green");

        /// <summary>A note that is nothing but code: every line mono, syntax toggle already on.</summary>
        private static FlowDocument DemoCode(params string[] lines)
        {
            var d = new FlowDocument { Tag = SyntaxTag };
            foreach (var l in lines) d.Blocks.Add(DemoMono(l));
            return d;
        }

        // ---- Preview detection demos (1.1.7, #14) ------------------------------------------
        // Three notes that show the three outcomes side by side. Each is a plausible note a tech
        // would actually keep; the point is what the Preview button does, not the content.

        /// <summary>Genuine markdown - headers, bold, a link and a fenced block. Detection fires
        /// and the Preview button appears (note left on Automatic).</summary>
        private static FlowDocument DemoMarkdownRunbook()
        {
            var d = new FlowDocument { Tag = SyntaxTag };
            d.Blocks.Add(DemoP("# Failover runbook"));
            d.Blocks.Add(DemoP(""));
            d.Blocks.Add(DemoP("## Before you start"));
            d.Blocks.Add(DemoP("Confirm the **secondary** is in sync before touching anything."));
            d.Blocks.Add(DemoP("Escalation contacts: [on-call rota](https://example.com/rota)"));
            d.Blocks.Add(DemoP(""));
            d.Blocks.Add(DemoP("## Cutover"));
            d.Blocks.Add(DemoP("```"));
            d.Blocks.Add(DemoMono("Set-DnsServerResourceRecord -ZoneName corp.local -Name vpn"));
            d.Blocks.Add(DemoP("```"));
            d.Blocks.Add(DemoP(""));
            d.Blocks.Add(DemoP("> Roll back if replication lag exceeds 5 minutes."));
            return d;
        }

        /// <summary>Plain text that TRIPS the heuristic - dash bullets plus numbered steps are two
        /// of the eight signals, which is all it takes. Set to Always plain text, so the Preview
        /// button stays away where the old build would have offered one (#14, MrPapaya-JRR).</summary>
        private static FlowDocument DemoPlainChecklist()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Meadowbrook cutover - Saturday 0600"));
            d.Blocks.Add(DemoP("================================="));
            d.Blocks.Add(DemoP(""));
            d.Blocks.Add(DemoP("- badge + van keys"));
            d.Blocks.Add(DemoP("- spare SFP, patch leads, label printer"));
            d.Blocks.Add(DemoP("- printed rack diagram"));
            d.Blocks.Add(DemoP(""));
            d.Blocks.Add(DemoP("1. power down the old switch stack"));
            d.Blocks.Add(DemoP("2. move uplinks port for port"));
            d.Blocks.Add(DemoP("3. verify each VLAN before leaving site"));
            d.Blocks.Add(DemoP(""));
            d.Blocks.Add(DemoP("---------------------------------"));
            d.Blocks.Add(DemoP("Sign-off: ______________________"));
            return d;
        }

        /// <summary>A pasted HTML fragment. Three or more tags win over the markdown signals, so
        /// this detects as HTML and previews defused (no scripts, handlers, frames or js: URLs).</summary>
        private static FlowDocument DemoHtmlSnippet()
        {
            var d = new FlowDocument { Tag = SyntaxTag };
            d.Blocks.Add(DemoP("Status banner the client wants on their intranet page:"));
            d.Blocks.Add(DemoMono("<div class=\"notice\">"));
            d.Blocks.Add(DemoMono("  <h3>Planned maintenance</h3>"));
            d.Blocks.Add(DemoMono("  <p>File server offline Sat 06:00-09:00.</p>"));
            d.Blocks.Add(DemoMono("  <p><a href=\"https://example.com/status\">Live status</a></p>"));
            d.Blocks.Add(DemoMono("</div>"));
            return d;
        }

        // ---- SketchPad demo drawings -------------------------------------------------------
        // Fabricated SketchPad sketches so screenshots can show the pad's output. Each is a real
        // List<SketchObject> flattened to an in-note image AND carried as an editable payload
        // (Sketch.SetData), exactly like a live "Print to note" - Add() persists it to the sketch
        // side table, so double-clicking the image in the demo reopens it in the pad. Colors are
        // ARGB (#AARRGGBB); the flatten pass nudges near-white/near-black so they read on any paper.

        private static SketchObject SkRect(double x0, double y0, double x1, double y1, string color, double w = 3, string? fill = null)
            => new() { Kind = SketchKind.Rect, Color = color, Width = w, Fill = fill, Pts = [x0, y0, x1, y1] };

        private static SketchObject SkEllipse(double x0, double y0, double x1, double y1, string color, double w = 3, string? fill = null)
            => new() { Kind = SketchKind.Ellipse, Color = color, Width = w, Fill = fill, Pts = [x0, y0, x1, y1] };

        private static SketchObject SkLine(double x0, double y0, double x1, double y1, string color, double w = 3)
            => new() { Kind = SketchKind.Line, Color = color, Width = w, Pts = [x0, y0, x1, y1] };

        private static SketchObject SkArrow(double x0, double y0, double x1, double y1, string color, double w = 4)
            => new() { Kind = SketchKind.Arrow, Color = color, Width = w, Pts = [x0, y0, x1, y1] };

        private static SketchObject SkText(double x, double y, string text, string color, double size = 22, bool bold = false)
            => new() { Kind = SketchKind.Text, Color = color, X = x, Y = y, Text = text, FontSize = size, Bold = bold };

        private static SketchObject SkFree(string color, double w, params double[] pts)
            => new() { Kind = SketchKind.Freehand, Color = color, Width = w, Pts = [.. pts] };

        // Flatten a sketch to an editable in-note image (payload attached so double-click reopens it).
        private static Image SketchImage(List<SketchObject> objs)
        {
            var bmp = SketchModel.RenderObjects(objs, Sketch.CanvasW, Sketch.CanvasH);
            var img = new Image { Source = bmp, MaxWidth = 560, Stretch = Stretch.Uniform };
            FixImage(img);                                    // high-quality scaling, like pasted images
            Sketch.SetData(img, SketchModel.Serialize(objs));
            return img;
        }

        // The sketch as its own paragraph (InlineUIContainer > Image, the shape ImportImage uses).
        private static Paragraph SketchPara(List<SketchObject> objs)
        {
            var p = new Paragraph();
            p.Inlines.Add(new InlineUIContainer(SketchImage(objs)));
            return p;
        }

        private static FlowDocument DemoRackSketch()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Rack elevation for the MDF after the firewall swap - sketched on the bench and printed straight in.", bold: true));
            d.Blocks.Add(SketchPara(RackSketch()));
            d.Blocks.Add(DemoP("Double-click the sketch to reopen it in the pad and mark up the next change.", color: "#7A8CA3"));
            return d;
        }

        private static List<SketchObject> RackSketch()
        {
            const string SLATE = "#FF9AA7B8", BLUE = "#FF50AEE8", RED = "#FFDD504B",
                         GREEN = "#FF1EA54C", ORANGE = "#FFE8962C", INK = "#FFDCE3EE";
            return
            [
                SkText(300, 16, "MDF rack - Meadowbrook", INK, 24, true),
                SkRect(300, 58, 520, 470, SLATE, 3),
                SkRect(308, 66, 512, 106, RED, 2, "#26DD504B"),     SkText(318, 74, "Firewall (new)", RED, 20, true),
                SkRect(308, 114, 512, 154, BLUE, 2, "#2650AEE8"),   SkText(318, 122, "Core switch", BLUE, 20, true),
                SkRect(308, 162, 512, 202, BLUE, 2, "#2650AEE8"),   SkText(318, 170, "Access switch", BLUE, 20, true),
                SkRect(308, 210, 512, 250, GREEN, 2, "#261EA54C"),  SkText(318, 218, "Patch panel", GREEN, 20, true),
                SkRect(308, 388, 512, 460, ORANGE, 2, "#26E8962C"), SkText(318, 412, "UPS 1500VA", ORANGE, 20, true),
                SkArrow(640, 86, 516, 86, RED, 4),    SkText(556, 60, "WAN in", RED, 18),
                SkArrow(640, 134, 516, 134, BLUE, 4), SkText(556, 108, "uplink", BLUE, 18),
                SkText(300, 478, "gap = spare U for the NAS", INK, 15),
            ];
        }

        private static FlowDocument DemoWifiSketch()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Warehouse coverage sketch from the walk-through. Two good APs; the dock corner is the dead spot.", bold: true));
            d.Blocks.Add(SketchPara(WifiSketch()));
            d.Blocks.Add(DemoP("Rough, but enough to place the third AP. Reopens editable for the final plan.", color: "#7A8CA3"));
            return d;
        }

        private static List<SketchObject> WifiSketch()
        {
            const string SLATE = "#FF9AA7B8", BLUE = "#FF50AEE8", RED = "#FFDD504B",
                         GREEN = "#FF1EA54C", INK = "#FFDCE3EE";
            return
            [
                SkText(40, 16, "Warehouse - AP coverage", INK, 24, true),
                SkRect(40, 58, 760, 452, SLATE, 3),
                SkLine(280, 58, 280, 452, SLATE, 2),
                SkText(120, 70, "Office", INK, 20),
                SkText(470, 70, "Warehouse floor", INK, 20),
                SkEllipse(60, 150, 240, 340, GREEN, 2, "#1C1EA54C"),
                SkEllipse(140, 235, 160, 255, BLUE, 3, "#3350AEE8"),
                SkText(120, 118, "AP1", BLUE, 20, true),
                SkEllipse(330, 150, 570, 380, GREEN, 2, "#1C1EA54C"),
                SkEllipse(440, 255, 460, 275, BLUE, 3, "#3350AEE8"),
                SkText(430, 118, "AP2", BLUE, 20, true),
                SkFree(RED, 5, 628, 352, 672, 392, 712, 432),
                SkFree(RED, 5, 712, 352, 668, 392, 628, 432),
                SkText(596, 312, "dead spot", RED, 22, true),
                SkText(600, 340, "needs AP3", RED, 16),
                SkText(628, 436, "dock", INK, 16),
            ];
        }
    }
}
