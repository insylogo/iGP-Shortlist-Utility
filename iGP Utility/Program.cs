using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.IO;
using Microsoft.Extensions.Options;

namespace iGP_Utility
{
    class IGPOptions
    {

        public bool IncludeDrivers { get; set; }
        public bool IncludeDesigners { get; set; }
        public bool IncludeEngineers { get; set; }
        public bool IncludeDoctors { get; set; }
        public int StartingPage { get; set; }
        public int MinimumStaffLevel { get; set; }
        public int MinimumDriverTalent { get; set; }
        public int MinimumDriverStamina { get; set; }
        public int MaximumDriverWeight { get; set; }
        public int MaximumDriverAge { get; set; }
        public int MaximumContractRemaining { get; set; }
        public int MaximumLevel { get; set; }
        public int RequestDelay { get; set; }
        public string HofType { get; set; }
        public string uc1 { get; set; }
        public string uc2 { get; set; }
        public string PHPSESSID { get; set; }


        public CookieContainer GetIdentifyingCookies()
        {
            var cookies = new CookieContainer();
            cookies.Add(new Uri("https://igpmanager.com/", UriKind.Absolute), new Cookie(nameof(uc1), uc1));
            cookies.Add(new Uri("https://igpmanager.com/", UriKind.Absolute), new Cookie(nameof(uc2), uc2));
            cookies.Add(new Uri("https://igpmanager.com/", UriKind.Absolute), new Cookie(nameof(PHPSESSID), PHPSESSID));

            cookies.Add(new Uri("https://igpmanager.com/", UriKind.Absolute), new Cookie("dst", "0"));
            cookies.Add(new Uri("https://igpmanager.com/", UriKind.Absolute), new Cookie("cookies", "1"));

            return cookies;
        }

    }
    class Program
    {

        static IConfigurationRoot Configuration { get; set; }

        static IGPOptions CurrentOptions { get; set; }


        static string[] goodStrengths = { "Acceleration"/*, "Braking", "Handling", "Downforce" */};
        static string[] goodWeaknesses = { "Reliability", "Cooling" };

        static void Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
                                .SetBasePath(Directory.GetCurrentDirectory())
                                .AddJsonFile("appsettings.json", false, true);


            Configuration = builder.Build();            

            CurrentOptions = new IGPOptions();
            Configuration.GetReloadToken().RegisterChangeCallback(reloadCallback, null);

            var configOptions = new ConfigureFromConfigurationOptions<IGPOptions>(Configuration);
            configOptions.Configure(CurrentOptions);
            

            var thread = AddStaff();
            thread.Wait();

            Console.ReadKey();
        }

        private static void reloadCallback(object obj)
        {
            Configuration.Bind(CurrentOptions);
            Configuration.GetReloadToken().RegisterChangeCallback(reloadCallback, null);
        }

        private static async Task AddStaff()
        {
            for (int i = CurrentOptions.StartingPage; i >= 0; i--)
            {

                try
                {
                    Console.WriteLine($"Starting page {i}");
                    var hof = await GetHoF(i);

                    var managers = Regex.Matches(hof, "d=profile&manager=([0-9]+)");
                    foreach (Match match in managers)
                    {
                        var manager = await Fetch(match.Captures[0].Value);
                        var result = JsonConvert.DeserializeAnonymousType(manager, new { vars = new { design = "", engineer = "", train = "", driver1 = "", driver2 = "" } });

                        if (CurrentOptions.IncludeDesigners)
                        {
                            await Check(result.vars.design, "Designer");
                        }

                        if (CurrentOptions.IncludeDoctors)
                        {
                            // doctor = "train";
                            await Check(result.vars.train, "Doctor");
                        }

                        if (CurrentOptions.IncludeEngineers)
                        {
                            // technical director = "engineer"
                            await Check(result.vars.engineer, "Engineer");
                        }

                        if (CurrentOptions.IncludeDrivers)
                        {
                            await Check(result.vars.driver1, "Driver");
                            await Check(result.vars.driver2, "Driver");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private static async Task Check(string unparsedPerson, string staffType)
        {
            var staffMatch = Regex.Match(unparsedPerson, "d=.+&id=([0-9]+)");
            if (staffMatch.Length == 0)
            {
                return;
            }
            var staffId = staffMatch.Captures[0].Value;
            var staff = await Fetch(staffId);

            var parsedStaff = JsonConvert.DeserializeAnonymousType(staff, new { vars = new { starrating = "", skilltable = "", options = "", contract = "", sTalent = "", sStamina = "", sWeight = "", sAge = "" } });

            if (!parsedStaff.vars.options.Contains("&#xf359;"))
            {
                // already added someone from this page before, break out of this page into the next.
                throw new Exception("Staff member already shortlisted. Skipping page.");
            }

            Regex attributes = new Regex("(Acceleration|Braking|Cooling|Fuel economy|Handling|Cooling|Downforce|Reliability|Tyre economy)");
            Regex starLevel = new Regex(@"\(([0-9]+)\)");
            Regex contract = new Regex(@"([0-9]+) race");
            Regex numberMatch = new Regex("[0-9]+");

            var designerSkills = !string.IsNullOrEmpty(parsedStaff.vars.skilltable) ? attributes.Matches(parsedStaff.vars.skilltable).Cast<Match>() : null;
            bool isDesigner = designerSkills?.Any() != null;
            bool isDriver = !string.IsNullOrWhiteSpace(parsedStaff.vars.sWeight);

            var level = int.Parse(starLevel.Match(parsedStaff.vars.starrating).Groups[1].Value);
            var contractRemaining = int.Parse(contract.Match(parsedStaff.vars.contract).Groups[1].Value);

            if (level > CurrentOptions.MaximumLevel || contractRemaining > CurrentOptions.MaximumContractRemaining)
            {
                return;
            }

            if (isDriver)
            {
                var talent = int.Parse(numberMatch.Match(parsedStaff.vars.sTalent).Value);
                var weight = int.Parse(numberMatch.Match(parsedStaff.vars.sWeight).Value);
                var age = int.Parse(numberMatch.Match(parsedStaff.vars.sAge).Value);
                if (talent < CurrentOptions.MinimumDriverTalent || weight > CurrentOptions.MaximumDriverWeight || age > CurrentOptions.MaximumDriverAge)
                {
                    return;
                }

                Console.WriteLine($"BING! {staffType} Found! Level: {level} - Talent: {talent} - Weight: {weight} - Age: {age} - Contract: {contractRemaining}");
                await Shortlist(staffMatch.Groups[1].Value, 3);

                return;
            }
            else
            {
                // non-driver staff have minimum level requirement
                if (level < CurrentOptions.MinimumStaffLevel)
                {
                    return;
                }

                if (isDesigner)
                {
                    // check designer features..
                    var strength = designerSkills.First().Value; // will be the first one captured
                    var weakness = designerSkills.Last().Value; // will be the last one captured
                    if (!goodStrengths.Contains(strength) || !goodWeaknesses.Contains(weakness))
                    {
                        // not a good enough designer..
                        return;
                    }
                    Console.WriteLine($"{staffType} Found! Level: {level} - Strength: {strength} - Weakness: {weakness}. - Contract: {contractRemaining}");
                }
                else
                {
                    Console.WriteLine($"{staffType} Found! Level: {level} - Contract: {contractRemaining}");
                }

                await Shortlist(staffMatch.Groups[1].Value, 2);
                return;
            }
        }

        static async Task<string> GetHoF(int page)
        {
            using (var handler = new HttpClientHandler() { CookieContainer = CurrentOptions.GetIdentifyingCookies(), MaxConnectionsPerServer = 1 })
            using (var client = new HttpClient(handler))
            {
                await Task.Delay(CurrentOptions.RequestDelay);
                return await client.GetStringAsync($"https://igpmanager.com/content/misc/igp/ajax/hof.php?category={CurrentOptions.HofType}&page={page}");
            }
        }

        static async Task<string> Fetch(string requestPath)
        {
            using (var handler = new HttpClientHandler() { CookieContainer = CurrentOptions.GetIdentifyingCookies(), MaxConnectionsPerServer = 1 })
            using (var client = new HttpClient(handler))
            {
                await Task.Delay(CurrentOptions.RequestDelay);
                return await client.GetStringAsync($"https://igpmanager.com/index.php?action=fetch&{requestPath}");
            }
        }


        static async Task<HttpResponseMessage> Shortlist(string staffId, int staffType)
        {
            using (var handler = new HttpClientHandler() { CookieContainer = CurrentOptions.GetIdentifyingCookies(), MaxConnectionsPerServer = 1 })
            using (var client = new HttpClient(handler))
            {
                return await client.GetAsync($"https://igpmanager.com/index.php?action=send&type=shortlist&eType={staffType}&eId={staffId}&jsReply=shortlist");
            }
        }
    }
}
