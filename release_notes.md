# Release Notes

#### v1.0.1 2026-07-??

* Add `loglevel` setting to `[app]` section (default is Warning)
* Add `validate` interactive command to check `uptime.conf`
* Improve tag splitting (opened related [bug report](https://github.com/dotnet/diagnostics/issues/5935))
* Add `[processtags]` config to attach target data like filename, etc.
* Add `[hosttags]` config to attach server data like hostname, env-vars, etc.
* Improved logging, added a console logger for service mode output
* Show Windows permissions reminder when running service as user
* README warning about provider-wildcard overhead and filtering
* README clarification that Uptime is not "transparent" to Collectors
* Bugfix - provider process-filter
* Bugfix - include/exclude specifier parsing
* Refactor - config parsing to support validation (including startup)
* Refactor - unified namespace (now that it's a single codebase)

#### v1.0.0 2026-07-19
 
* Initial release (get the basics working!)
 