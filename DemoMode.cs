using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Services;

namespace KillerNotes
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

            void Add(string title, double daysAgo, FlowDocument doc, bool feature = false, string? tags = null)
            {
                long id = CreateNoteFromDocument(title, doc);   // ImportExport.cs
                var created = now.AddDays(-daysAgo);
                var modified = created.AddHours(2 + (daysAgo % 5) * 7);
                if (modified > now) modified = now.AddMinutes(-14);
                NoteStore.SetTimestamps(id, created, modified);
                if (tags != null) NoteStore.SetNoteTags(id, tags);
                if (feature) showcase = id;
            }

            // Swap the color-named defaults for the MSP-flavored set, then tag the notes.
            foreach (var t in NoteStore.ListTags()) NoteStore.DeleteTag(t.Name);
            foreach (var t in DemoTags) NoteStore.AddTag(t.Name, t.Color);

            Add("Northwind Dental - site visit", 38, DemoSiteVisit(), tags: "On-site, Reference");
            Add("Firewall swap - Meadowbrook Vet", 31, DemoFirewallSwap(), feature: true, tags: "On-site, Network");
            Add("PowerShell one-liners", 27, DemoPowerShell(), tags: "Reference");
            Add("Switch port map - Oakfield Law", 20, DemoPortMap(), tags: "Network, Reference");
            Add("UPS runtimes", 16, DemoUps(), tags: "Reference, Follow-up");
            Add("New tech onboarding", 12, DemoOnboarding(), tags: "Reference");
            Add("RMM agent cleanup", 8, DemoRmm(), tags: "Follow-up");
            Add("Parts drawer inventory", 5, DemoParts(), tags: "Reference");
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
                new[] { "Setting", "Old unit", "New unit" },
                new[] { "WAN IP", "203.0.113.10 /29", "203.0.113.10 /29" },
                new[] { "LAN GW", "192.0.2.1 /24", "192.0.2.1 /24" },
                new[] { "VPN peers", "3 (see vendor sheet)", "re-key all 3" },
                new[] { "DNS fwd", "198.51.100.53", "198.51.100.53" },
                new[] { "Mgmt access", "LAN only", "LAN + mgmt VLAN 99" }));
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
            var d = new FlowDocument();
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
                new[] { "Port", "VLAN", "Goes to" },
                new[] { "1-8", "10", "Workstations, front office" },
                new[] { "9-12", "10", "Workstations, paralegals" },
                new[] { "13-16", "20", "Phones" },
                new[] { "17-18", "30", "Printers" },
                new[] { "19-22", "10", "Conference rooms" },
                new[] { "23", "99", "AP uplink (trunk)" },
                new[] { "24", "trunk", "Uplink to firewall" }));
            d.Blocks.Add(DemoP("Spare drops in the ceiling above suite B - unterminated."));
            return d;
        }

        private static FlowDocument DemoUps()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Measured on battery with normal load, this quarter:"));
            d.Blocks.Add(DemoTable(
                new[] { "Location", "Model", "Runtime" },
                new[] { "MDF", "1500VA rack", "22 min" },
                new[] { "Front desk", "650VA tower", "9 min" },
                new[] { "Server closet", "3000VA rack", "41 min" }));
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
                new[] { "Part", "Qty", "Reorder at" },
                new[] { "Cat6 patch 1m", "18", "10" },
                new[] { "Cat6 patch 3m", "7", "5" },
                new[] { "SFP+ DAC 3m", "4", "2" },
                new[] { "RJ45 ends (bag)", "2", "1" },
                new[] { "PSU tester", "1", "-" }));
            return d;
        }

        private static FlowDocument DemoScratch()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("call back about the NAS quote - they want the 4-bay after all"));
            d.Blocks.Add(DemoP("ticket #4183 waiting on ISP"));
            return d;
        }
    }
}
