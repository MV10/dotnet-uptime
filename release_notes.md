# Release Notes

#### v1.0.1 2026-07-??

* Additional interactive commands:
  * `validate` checks the `uptime.conf` configuration file
  * `stats` outputs Uptime's own metrics to console
  * `summary` outputs Uptime's monitored processes info to console
* Additional configuration file changes: 
  * `[app] loglevel` (default is Warning)
  * `[app] summarycommand` disabled (default) / enabled (unsecured) / elevated (require root/Admin)
  * `[app] redactpayload` sensitive-data redaction for OTLP `process.command_line` (default true)
  * `[processtags]` section, attaches target process data like filename, etc.
  * `[hosttags]` section, attaches server data like hostname, env-vars, etc.
  * `[include]` and `[exclude]` assembly-matching logic (supplements filename-matching)
* Improve tag splitting (opened related .NET [bugs reported](https://github.com/dotnet/diagnostics/issues/5935))
* Full code audit to ensure full coverage for error handling and logging
* Added a console logger for service mode output
* Rejected-PID caching avoids re-querying certain processes unnecessarily
* Add Uptime-specific metrics for OTLP export (`dotnet-uptime.self`)
* Add `stats_metrics.md` repository document explaining Uptime's custom metrics
* Add sensitive-data redaction rules for command-line content
* Add broken diagnostic pipe re-connect rules and logging
* Prevent starting in service mode if it is already running
* Prevent OTEL transmission on interactive PID monitoring if service is running
* Windows - show permissions limitations reminder when running service as user
* High-detail error-handling coverage review (e.g. task cancellation, etc.)
* README warning about provider-wildcard overhead and filtering
* README clarification that Uptime is not "transparent" to Collectors
* README explanation of command-line sensitive-data redaction behaviors
* README clarification about where log events are captured (OS dependent)
* Bugfix - provider process-filter
* Bugfix - `[include]`/`[exclude]` specifier parsing
* Refactor - config parsing to support validation (including startup)
* Refactor - unified namespace (now that it's a single codebase)

#### v1.0.0 2026-07-19
 
* Initial release (get the basics working!)
 