using System;
using System.IO;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        string path = @"C:\Users\andre\Desktop\Company-Employees-Management\src\Frontend\CompanyEmployees.Web\Components\Employee\Pages\CompanyDirectory.razor";
        string content = File.ReadAllText(path);
        
        // 1. Rename "Global World Map" breadcrumb to "Romania Map"
        content = content.Replace("🌍 Global World Map", "🇷🇴 Romania Map");
        content = content.Replace("Global Continents", "Romania Map");
        
        // 2. Rename the toggle buttons
        content = content.Replace("Global Map\n        </FluentButton>", "Romania Map\n        </FluentButton>");
        content = content.Replace("Globe()", "Map()");
        
        // 3. Replace the _continents render block with the SVG map
        string oldRenderBlock = @"    @* The hand-drawn vector map was the one thing on this page with no Fluent equivalent;
       the same information is now a card per region, which also makes the counts readable
       without hovering. *@
    <FluentGrid Spacing=""3"" Style=""margin-top:16px;"">
        @foreach (var continent in _continents)
        {
            <FluentGridItem xs=""12"" sm=""6"" md=""4"">
                <FluentCard Style=""padding:16px; height:100%;"">
                    <FluentStack Orientation=""Orientation.Vertical"" VerticalGap=""12"">
                        <FluentStack Orientation=""Orientation.Horizontal"" HorizontalGap=""8""
                                     VerticalAlignment=""VerticalAlignment.Center"" Width=""100%"">
                            <FluentStack Orientation=""Orientation.Vertical"" VerticalGap=""0"">
                                <FluentLabel Typo=""Typography.Subject"" Weight=""FontWeight.Bold"">
                                    @continent.FlagEmoji @continent.Name
                                </FluentLabel>
                                <FluentLabel Typo=""Typography.Body"" Color=""Color.Custom"" CustomColor=""var(--neutral-foreground-hint)"" Class=""app-caption"">
                                    @continent.RegionCode
                                </FluentLabel>
                            </FluentStack>
                            <FluentSpacer />
                            <FluentBadge Appearance=""Appearance.Neutral"">
                                @continent.ActiveCountriesCount Countries
                            </FluentBadge>
                        </FluentStack>

                        <FluentDivider Role=""DividerRole.Presentation"" />

                        <FluentStack Orientation=""Orientation.Vertical"" VerticalGap=""6"">
                            <FluentStack Orientation=""Orientation.Horizontal"" Width=""100%"">
                                <FluentLabel Typo=""Typography.Body"" Color=""Color.Custom"" CustomColor=""var(--neutral-foreground-hint)"">Regional Leader</FluentLabel>
                                <FluentSpacer />
                                <FluentLabel Typo=""Typography.Body"" Weight=""FontWeight.Bold"" Color=""Color.Custom"" CustomColor=""var(--accent-foreground-rest)"">
                                    @continent.LeadName
                                </FluentLabel>
                            </FluentStack>
                            <FluentStack Orientation=""Orientation.Horizontal"" Width=""100%"">
                                <FluentLabel Typo=""Typography.Body"" Color=""Color.Custom"" CustomColor=""var(--neutral-foreground-hint)"">Hubs</FluentLabel>
                                <FluentSpacer />
                                <FluentLabel Typo=""Typography.Body"">@continent.ActiveHubsCount Active Hubs</FluentLabel>
                            </FluentStack>
                            <FluentStack Orientation=""Orientation.Horizontal"" Width=""100%"">
                                <FluentLabel Typo=""Typography.Body"" Color=""Color.Custom"" CustomColor=""var(--neutral-foreground-hint)"">Workforce</FluentLabel>
                                <FluentSpacer />
                                <FluentLabel Typo=""Typography.Body"">@continent.TotalEmployeesCount Employees</FluentLabel>
                            </FluentStack>
                        </FluentStack>
                    </FluentStack>
                </FluentCard>
            </FluentGridItem>
        }
    </FluentGrid>";

        string newRenderBlock = @"    <div style=""position: relative; width: 100%; height: 650px; background: radial-gradient(circle at 50% 50%, #f0f7fa 0%, #d8e8f0 100%); overflow: hidden; margin-top: 16px; border-radius: 8px;"">
        <svg width=""100%"" height=""100%"" viewBox=""0 0 1000 650"" preserveAspectRatio=""xMidYMid meet"">
            <defs>
                <style>
                    .romania-county-path {
                        fill: #ffffff;
                        stroke: #008899;
                        stroke-width: 1.4px;
                        stroke-linejoin: round;
                        stroke-linecap: round;
                        transition: fill 0.25s ease, stroke 0.25s ease, stroke-width 0.25s ease, filter 0.25s ease;
                        filter: drop-shadow(0 2px 5px rgba(0, 70, 90, 0.15));
                        cursor: pointer;
                    }
                    .romania-county-path.has-presence {
                        fill: #e0f4f7;
                        stroke: #006677;
                        stroke-width: 1.8px;
                        filter: drop-shadow(0 3px 8px rgba(0, 90, 120, 0.25));
                    }
                    .romania-county-group:hover .romania-county-path,
                    .romania-county-path.selected {
                        fill: #cbeeef;
                        stroke: #005a66;
                        stroke-width: 2.5px;
                        filter: drop-shadow(0 4px 14px rgba(0, 120, 150, 0.4));
                    }

                    /* Siemens Pin styles - Light Mode */
                    .siemens-pin-dot {
                        fill: #008899;
                        filter: drop-shadow(0 2px 4px rgba(0, 0, 0, 0.3));
                    }
                    .siemens-pin-hq {
                        fill: #E68A00;
                        filter: drop-shadow(0 2px 6px rgba(0, 0, 0, 0.35));
                    }
                    .siemens-hq-pulse-ring {
                        fill: none;
                        stroke: #E68A00;
                        stroke-width: 2px;
                        animation: romaniaPulse 2s cubic-bezier(0.25, 0.8, 0.25, 1) infinite;
                    }
                    .siemens-hub-pulse-ring {
                        fill: none;
                        stroke: #008899;
                        stroke-width: 1.8px;
                        animation: romaniaPulse 2.2s cubic-bezier(0.25, 0.8, 0.25, 1) infinite;
                    }

                    /* City Tag Badges Light */
                    .siemens-city-tag-hq {
                        fill: #ffffff;
                        stroke: #E68A00;
                        stroke-width: 1.5px;
                        filter: drop-shadow(0 2px 6px rgba(0, 0, 0, 0.15));
                    }
                    .siemens-city-text-hq {
                        font-family: 'Roboto', 'Inter', sans-serif;
                        font-size: 11px;
                        font-weight: 800;
                        fill: #C07000;
                        letter-spacing: 0.5px;
                    }
                    .siemens-city-tag {
                        fill: #ffffff;
                        stroke: #008899;
                        stroke-width: 1.2px;
                        filter: drop-shadow(0 2px 5px rgba(0, 0, 0, 0.12));
                    }
                    .siemens-city-text {
                        font-family: 'Roboto', 'Inter', sans-serif;
                        font-size: 10px;
                        font-weight: 700;
                        fill: #006677;
                        letter-spacing: 0.4px;
                    }

                    @keyframes romaniaPulse {
                        0% { r: 6; opacity: 1; }
                        100% { r: 18; opacity: 0; }
                    }
                </style>

                <pattern id=""romaniaCyberGrid"" width=""40"" height=""40"" patternUnits=""userSpaceOnUse"">
                    <path d=""M 40 0 L 0 0 0 40"" fill=""none"" stroke=""rgba(0, 136, 153, 0.07)"" stroke-width=""1""/>
                </pattern>
            </defs>

            <!-- Grid Background -->
            <rect width=""100%"" height=""100%"" fill=""url(#romaniaCyberGrid)"" />

            <!-- Romanian Counties Group -->
            @foreach (var county in _romaniaCounties)
            {
                bool isSelected = _activeRomaniaCounty?.Id == county.Id;
                <g class=""romania-county-group @(isSelected ? ""county-active"" : """")""
                   @onclick=""() => _activeRomaniaCounty = county""
                   cursor=""pointer"">
                    
                    <path d=""@county.SvgPath""
                          class=""romania-county-path @(county.HasSiemensPresence ? ""has-presence"" : """") @(isSelected ? ""selected"" : """")"" />
                </g>
            }

            <!-- Accentuated Siemens Presence Cities -->
            @foreach (var county in _romaniaCounties.Where(c => c.HasSiemensPresence))
            {
                bool isHubSelected = _activeRomaniaCounty?.Id == county.Id;
                string tagText = county.IsHQ ? ""⭐ BUCUREȘTI HQ"" : county.CountySeat.ToUpper();
                
                string tagTransform = county.CountySeat switch
                {
                    ""Brașov"" => ""translate(10, 8)"",
                    ""Sibiu"" => ""translate(-75, -12)"",
                    ""Craiova"" => ""translate(10, 8)"",
                    ""Timișoara"" => ""translate(10, -12)"",
                    ""București"" => ""translate(14, -14)"",
                    _ => ""translate(10, -12)""
                };

                int badgeWidth = county.IsHQ ? 135 : (county.CountySeat.Length > 8 ? 120 : 90);

                <g class=""siemens-hub-pin-group @(isHubSelected ? ""selected-hub"" : """")""
                   transform=""@($""translate({county.CenterX.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {county.CenterY.ToString(System.Globalization.CultureInfo.InvariantCulture)})"")""
                   @onclick=""() => _activeRomaniaCounty = county""
                   cursor=""pointer"">
                    @if (county.IsHQ)
                    {
                        <circle r=""14"" class=""siemens-hq-pulse-ring"" />
                        <circle r=""7"" class=""siemens-pin-hq"" />
                        <circle r=""3"" fill=""#ffffff"" />
                        <g transform=""@tagTransform"">
                            <rect width=""@badgeWidth"" height=""24"" rx=""12"" class=""siemens-city-tag-hq"" />
                            <text x=""12"" y=""16"" class=""siemens-city-text-hq"">@tagText</text>
                        </g>
                    }
                    else
                    {
                        <circle r=""10"" class=""siemens-hub-pulse-ring"" />
                        <circle r=""5"" class=""siemens-pin-dot"" />
                        <circle r=""2"" fill=""#ffffff"" />
                        <g transform=""@tagTransform"">
                            <rect width=""@badgeWidth"" height=""20"" rx=""10"" class=""siemens-city-tag"" />
                            <text x=""10"" y=""14"" class=""siemens-city-text"">@tagText</text>
                        </g>
                    }
                </g>
            }
        </svg>

        @if (_activeRomaniaCounty != null)
        {
            <div style=""position: absolute; left: 40px; top: 40px; max-width: 320px; z-index: 10;"">
                <FluentCard Style=""background: rgba(255, 255, 255, 0.95); backdrop-filter: blur(4px);"">
                    <FluentStack Orientation=""Orientation.Vertical"" VerticalGap=""12"" Style=""padding: 16px;"">
                        <FluentStack Orientation=""Orientation.Horizontal"" VerticalAlignment=""VerticalAlignment.Center"" HorizontalGap=""8"">
                            <span style=""font-size: 24px;"">🇷🇴</span>
                            <FluentStack Orientation=""Orientation.Vertical"" VerticalGap=""0"">
                                <FluentLabel Typo=""Typography.Subject"" Weight=""FontWeight.Bold"">@_activeRomaniaCounty.Name</FluentLabel>
                                <FluentLabel Typo=""Typography.Body"" Color=""Color.Custom"" CustomColor=""var(--neutral-foreground-hint)"">@_activeRomaniaCounty.CountySeat (Reședință) · @_activeRomaniaCounty.Region</FluentLabel>
                            </FluentStack>
                            <FluentSpacer />
                            <FluentButton Appearance=""Appearance.Stealth"" IconStart=""@(new Icons.Regular.Size16.Dismiss())"" OnClick=""@(() => _activeRomaniaCounty = null)"" />
                        </FluentStack>
                        
                        @if (_activeRomaniaCounty.HasSiemensPresence)
                        {
                            <FluentDivider />
                            <FluentLabel Typo=""Typography.Body"" Weight=""FontWeight.Bold"">Siemens Sites:</FluentLabel>
                            @foreach (var hub in _activeRomaniaCounty.Hubs)
                            {
                                <div style=""padding: 8px; border-radius: 6px; background: rgba(0,136,153,0.10); color: #008899;"">
                                    <FluentStack Orientation=""Orientation.Horizontal"" VerticalAlignment=""VerticalAlignment.Center"">
                                        <FluentLabel Typo=""Typography.Body"" Weight=""FontWeight.Bold"">@(hub.IsHQ ? ""⭐ "" : ""📍 "")@hub.Name</FluentLabel>
                                        <FluentSpacer />
                                        <FluentLabel Typo=""Typography.Body"" Weight=""FontWeight.Bold"">@hub.EmployeesCount staff</FluentLabel>
                                    </FluentStack>
                                    <FluentLabel Typo=""Typography.Caption"">@hub.Division · @hub.Address</FluentLabel>
                                </div>
                            }
                        }
                    </FluentStack>
                </FluentCard>
            </div>
        }
    </div>";

        content = content.Replace(oldRenderBlock, newRenderBlock);

        // 4. Update the state code block to add variables for the Romania Map
        string stateCodeOld = @"    private List<ContinentModel> _continents = new();

    [CascadingParameter]";
        string stateCodeNew = @"    private List<ContinentModel> _continents = new();
    private List<RomaniaCountyModel> _romaniaCounties = new();
    private RomaniaCountyModel? _activeRomaniaCounty;

    [CascadingParameter]";
        content = content.Replace(stateCodeOld, stateCodeNew);

        string initCodeOld = @"    protected override async Task OnInitializedAsync()
    {
        InitializeContinentsData();";
        string initCodeNew = @"    protected override async Task OnInitializedAsync()
    {
        InitializeContinentsData();
        _romaniaCounties = RomaniaMapData.GetCounties();";
        content = content.Replace(initCodeOld, initCodeNew);

        File.WriteAllText(path, content);
    }
}
