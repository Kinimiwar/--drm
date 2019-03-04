# --drm
--drm is a de-drm web downloading tool with multiple supported websites

### Defeated DRMs/Protections

* Adobe Auth SP

### Supported Websites

* disneynow.go.com
* simpsonsworld.com

## Example Usage

The following will download all of Phineas and Ferb Season 1 starting with Episode 12 (Skipping Episodes 1 through 11).
It will ask you which quality (if available) you want to download. To automatically choose you can use --quality (e.x. 'best', '720', '720p', '1080', 1080p' are all valid options)
You only need to provide --ap-user and --ap-pass if you havent used that before for the set --ap-msoid

`--drm.exe --url "https://disneynow.go.com/shows/phineas-and-ferb/season-1" --episode 12 --ap-msoid "DTV" --ap-user "john@doe.xyz" --ap-pass "Password1"`
