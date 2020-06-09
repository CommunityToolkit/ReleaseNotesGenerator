using Octokit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReleaseNotes
{
    class Program
    {
        static GitHubClient _client = new GitHubClient(new ProductHeaderValue("uct-release-notes"));
        static Dictionary<string, User> _cachedUsers = new Dictionary<string, User>();

        // Contents of Tags.txt (prioritized order)
        static string[] _labels;

        // Contents of Config.txt
        static string _breakingHeader = "Breaking Changes";
        static string _breakingTag = "introduce breaking changes :boom:";
        static string _otherHeader = "Other Fixes";

        // GitHub App Info
        const string _clientId = "[CLIENT-ID]";
        const string _clientSecret = "[CLIENT-SECRET]";

        static string _tokenFilename = "token";
        static string _tagsFilename = "Tags.txt";
        static string _configFilename = "Config.txt";

        static int Main(string[] args)
        {
            if (File.Exists(_tagsFilename))
            {
                _labels = File.ReadAllLines(_tagsFilename);
            }
            else
            {
                Console.WriteLine("\r\nMissing Tags.txt file. Add GitHub labels to file one per line.");
                return 1;
            }

            if (File.Exists(_configFilename))
            {
                var lines = File.ReadAllLines(_configFilename);
                if (lines.Count() < 3)
                {
                    Console.WriteLine("Missing config lines. File Ignored.");
                }
                else
                {
                    _breakingHeader = lines[0];
                    _breakingTag = lines[1];
                    _otherHeader = lines[2];
                }
            }
            else
            {
                Console.WriteLine("\r\nMissing Config.txt file. Add Breaking Header, breaking label, and Other fixes label on 3 lines.");
                return 1;
            }

            if (args.Length == 1 && args[0] == "-auth")
            {
                Authorize().GetAwaiter().GetResult();
                return 0;
            }
            if (args.Length < 2)
            {
                Console.WriteLine("\r\nUsage: releasenotes <Repo Owner> <Repo Name> [-o outputfile]\r\n");
                return 1;
            }

            if (File.Exists(_tokenFilename))
            {
                var token = File.ReadAllText(_tokenFilename);
                _client.Credentials = new Credentials(token);
            }

            var task = PrintReleaseNotes(args[0], args[1]);
            var contents = task.GetAwaiter().GetResult();

            if (args.Length < 4)
            {
                Console.Write(contents);
            }
            else if (args[2] == "-o")
            {
                File.WriteAllText(args[3], contents);
            }

            return 0;
        }

        public static async Task Authorize()
        {
            var uri = _client.Oauth.GetGitHubLoginUrl(new OauthLoginRequest(_clientId));
            Process.Start(new ProcessStartInfo("cmd", $"/c start {uri.ToString()}"));
            Console.Write($"\r\nLaunching browser at {uri}.\r\nPlease log in and authorize application.\r\nAfter authorization, you will be redirected to an url that ends with code=[some_code]\r\nPlease copy the code, paste it here, and press enter.\r\n\r\ncode=");
            var code = Console.ReadLine();
            Console.Write("\r\n");

            if (String.IsNullOrEmpty(code))
                return;

            var request = new OauthTokenRequest(_clientId, _clientSecret, code);
            var token = await _client.Oauth.CreateAccessToken(request);

            File.WriteAllText(_tokenFilename, token.AccessToken);
        }

        private static async Task<string> PrintReleaseNotes(string repoOwner, string repoName)
        {
            var result = new StringBuilder();
            var notes = new Dictionary<string, List<string>>();

            foreach (var labelName in _labels)
            {
                notes.Add(labelName, new List<string>());
            }
            notes.Add(_otherHeader, new List<string>());
            notes.Add(_breakingHeader, new List<string>());

            try
            {
                var release = await _client.Repository.Release.GetLatest(repoOwner, repoName);
                var request = new PullRequestRequest()
                {
                    State = ItemStateFilter.Closed,
                    SortProperty = PullRequestSort.Updated
                };
                var options = new ApiOptions()
                {
                    PageCount = 1,
                    StartPage = 1
                };

                var prs = await _client.PullRequest.GetAllForRepository(repoOwner, repoName, request, options);
                var actionablePRs = prs.Where(pr => pr.Merged && pr.MergedAt > release.PublishedAt);
                do
                {
                    foreach (var pr in actionablePRs)
                    {
                        if (!_cachedUsers.TryGetValue(pr.User.Login, out var user))
                        {
                            user = await _client.User.Get(pr.User.Login);
                            _cachedUsers.Add(pr.User.Login, user);
                        }

                        var line = $"\t- {pr.Title} - [{user.Name ?? user.Login}]({user.HtmlUrl}) ([PR]({pr.HtmlUrl}))";

                        var issue = await _client.Issue.Get(repoOwner, repoName, pr.Number);
                        var matchedLabel = _labels.FirstOrDefault(label => issue.Labels.Select(i => i.Name).Contains(label));

                        notes[matchedLabel != null ? matchedLabel : _otherHeader].Add(line);

                        if (issue.Labels.Where(i => i.Name == _breakingTag).Count() > 0)
                            notes[_breakingHeader].Add(line);
                    }

                    options.StartPage++;
                    prs = await _client.PullRequest.GetAllForRepository(repoOwner, repoName, request, options);
                    actionablePRs = prs.Where(pr => pr.Merged && pr.MergedAt > release.PublishedAt);
                } while (actionablePRs.Count() > 0);
            }
            catch (Octokit.NotFoundException)
            {
                return "Repository not found, or repository does not have milestones or pull requests\r\n";
            }
            catch (Octokit.RateLimitExceededException ex)
            {
                return $"\r\n{ex.Message}\r\n\r\nTo authenticate, run: ReleaseNotes -auth\r\n\r\n";
            }

            foreach (var label in notes)
            {
                if (label.Value.Count > 0)
                {
                    result.AppendLine($"- {label.Key}");
                    foreach (var line in label.Value)
                    {
                        result.AppendLine(line);
                    }
                    result.AppendLine();
                }
            }

            return result.ToString();
        }
    }
}
