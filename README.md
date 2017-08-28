# ReleaseNotesGenerator
dotnet core console application to  generate release notes from Pull Requests for the [UWP Community Toolkit](https://github.com/Microsoft/UWPCommunityToolkit). 

Download binaries from here: https://github.com/nmetulev/ReleaseNotesGenerator/releases/tag/v1.0

## Usage:

*ReleaseNotes.exe [REPO-OWNER] [REPO-NAME] > [filename]* 
  - it will generate Markdown Release Notes by using Pull Requests. It will try to group Pull Requests based on their labels in one of these labels:
  "animations", "controls", "extensions", "services", "helpers", "connectivity", "notifications", "documentation", "sample app"
  
    Here is an example of what each line look like:
      - fix(InAppNotifications): use timer to detect notification dismiss event after timeout - [David Bottiau](https://github.com/Odonno) ([PR](https://github.com/Microsoft/UWPCommunityToolkit/pull/1434))

"*ReleaseNotes.exe -auth*" 
  - to generate an auth token that allows higher rate of api calls with the github api. On succesful auth, a new file called *token* will be generated that will contain the access token that will be used for all api calls 

## Github API key
To enable authentication in your own build, you will need to create a [new github app](https://github.com/settings/applications/new) and add add the generated clientId and clientsecret to Program.cs
