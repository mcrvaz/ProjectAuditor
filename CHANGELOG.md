# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.3.1] - 2020-10-23
* Page up/down key bug fixes
* Added dependencies view to Assets tab
* Move call tree to the bottom of the window
* Double-click on an asset selects it in the Project Window 
* Fixed Unity 2017 compatibility
* Fixed default selected assemblies
* Fixed Area names filtering
* Changed case-sensitive string search to be optional
* Added CI information to documentation
* Fixed call-tree serialization

## [0.3.0] - 2020-10-07
* Added auditing of assets in Resources folders
* Added shader warmup issues
* Reorganized UI filters and mute/unmute buttons in separate foldouts
* Fixed issues sorting within a group
* ExportToCSV improvements
* Better names for project settings issues

## [0.2.1] - 2020-05-22
* Improved text search UX
* Improved test coverage
* Fixed background assembly analysis
* Fixed lost issue location after domain reload
* Fix tree view selection when background analysis is enabled
* Fixed Yamato configuration
* Updated documentation

## [0.2.0] - 2020-04-27
* Added Boxing allocation analyzer
* Added Empty *MonoBehaviour* method analyzer
* Added *GameObject.tag* issue type to built-in analyzer
* Added *StaticBatchingAndHybridPackage* analyzer
* Added *Object.Instantiate* and *GameObject.AddComponent* issue types to built-in analyzer
* Added *String.Concat* issue type to built-in analyzer
* Added "experimental" allocation analyzer
* Added performance critical context analysis
* Detect *MonoBehaviour.Update/LateUpdate/FixedUpdate* as perf critical contexts
* Detect *ComponentSystem/JobComponentSystem.OnUpdate* as perf critical contexts
* Added critical-only UI filter
* Optimized UI refresh performance and Assembly analysis
* Added profiler markers
* Added background analysis support

## [0.1.0] - 2019-11-20
* Added Config asset support
* Added Mute/Unmute buttons
* Replaced Filters checkboxes with Popups
* Added Assembly column

## [0.0.4] - 2019-10-11
* Added Calling Method information
* Added Grouped view to Script issues
* Removed "Resolved" checkboxes
* Lots of bug fixes

## [0.0.3] - 2019-09-04
* Fixed Unity 2017.x backwards compatibility
* Added Progress bar
* Added Package whitelist
* Added Tooltips

## [0.0.2] - 2019-08-22

### First usable version

*Replaced placeholder database with real issues to look for*. This version also allows the user to Resolve issues.

## [0.0.1] - 2019-07-23

### This is the first release of *Project Auditor*

*Proof of concept, mostly developed during Hackweek 2019*.
