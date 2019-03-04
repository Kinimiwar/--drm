# --drm
--drm is a de-drm web downloading tool with multiple supported websites

# Defeated DRMs/Protections

* Adobe Auth SP

# Supported Websites

* simpsonsworld.com
* disneynow.go.com

# Dependencys

* ffmpeg
* mkvmerge
* subtitleedit
* ccextractor* (Not currently used but will be soon)

# Example Usage

The following will download all of Phineas and Ferb Season 1 starting with Episode 12 (Skipping Episodes 1 through 11).
You only need to provide --ap-user and --ap-pass if you havent used that before for the set --ap-msoid

`--drm.exe --url "https://disneynow.go.com/shows/phineas-and-ferb/season-1" --episode 12 --ap-msoid "DTV" --ap-user "john@doe.xyz" --ap-pass "Password1"`
