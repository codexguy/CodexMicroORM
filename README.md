# CodexMicroORM
An alternative to ORM's such as Entity Framework, offers light-weight database mapping to your existing CLR objects with minimal effort. Visit "Design Goals" on GitHub to see more rationale and guidance.

## Background
Why build a new ORM framework? After all, Entity Framework, nHibernate and plenty of others exist and are mature. Speaking of "mature," I built CodeXFramework V1.0 several years ago and it shares some of the "complaints" I've seen some people make about many mainstream ORM's: they can be "heavy," "bloated" and as much as we'd like them to be "unobtrusive" - sometimes they *are*.

Wouldn't it be nice if we could simply use our existing POCO (plain-old C# objects) and have them become *ORM-aware*? That's the ultimate design goal of CodexMicroORM: to give a similar vibe to what we got with "LINQ to Objects" several years ago. (Recall: that turned anything that was IEnumerable&lt;T&gt; into a fully LINQ-enabled list source - which opened up a whole new world of possibility!)

CodexMicroORM isn't necessarily going to do everything that other, larger ORM frameworks can do - that's the "micro" aspect. We'll leave some work to the framework user, favoring performance and memory use much of the time. That said, we do aim for *simplicity* as I hope the demo application illustrates. As one example, we can create a sample Person record in *one line of code*:

```c#
CEF.NewObject(new Person() { Name = "Bobby Tables", Age = 7, Gender = "M" }).DBSave();
```

The demo project shows several other non-trivial use cases - and more will follow. This initial 0.2 release of CodexMicroORM (aka "CEF" or "Codex Entity Framework") V0.2 is young enough that most core interfaces should stay the same, but we could be making some breaking changes as refactoring dictates.

## Design Goals
Diving more deeply, what does CodexMicroORM try to do *better* than other frameworks? What are the guiding principles that have influenced it to date?

* Support .NET Standard 2.0 as much as possible. (ICustomTypeProvider as one example isn't there, so for WPF we have the CodexMicroORM.BindingSupport project which is net461.)
* Entities can be *any* POCO object, period.
* Entities do not need to have any special "database-centric" properties (if they don't want - many will want them).
* Entities should be able to carry "extra" details that come from the database (aka "extended properties") - but these should be "non-obtrusive" against the bare POCO objects being used.
* Entities can be nested in what would be considered a normal object model (e.g. "IList&lt;Person&gt; Children" as a property on Person vs. a nullable ParentID which is a database/storage concept - but the DB way should be supported too!).
* Use convention-based approaches - but codify these conventions as global settings that describe how you prefer to work (e.g. do you not care about saving a "Last Updated By" field on records? The framework should support it but you should be able to opt out with one line of code at startup).
* Entities can be code-gened from databases, but is *not required at all*.
* Code-gen properties by entity / database object should be retrievable from a logical model (e.g. Erwin document), SQL-Hero (repository supports flexible user-defined properties), or some kind of file-based storage.
* Entities can be used in isolation of "contexts" like you see in EF: they really can be *any* object graph you like! This might seem like a small deal, but it really frees you up to work with objects however you like, with some conventions-based rules.
* Can be used as a "bridge": you may have POCO objects that don't implement INotifyPropertyChanged for example (and have no desire to change this!), but you might like this feature with minimal effort in some settings - you could with CEF create dynamic objects that deliver value-add and effectively wrap select POCO objects and use them for specific/limited situations (e.g. UI binding).
* *Anything* beyond simple POCO is considered a *service*.
* Entities can (and should) have "defaults" for what services they want to have supporting them:
	* Defaults can be set globally at AppDomain level.
	* Defaults can be set per entity based on code - not attributes like I've done in CodeXFramework v1 since in code can still be code-genned, can be called on "start" in most cases - attributes would force changing the nature of the POCO classes potentially (perhaps they aren't in partial classes, perhaps even if were partial classes, need to reference from external assemblies, etc.). Most of what you'd run on startup to configure the framework should be a) mostly generated from your model, b) fast (mainly just registration), c) self-contained (can go in a file / partial class).
	* Defaults can be overridden, per instance, both opting in and out of services.
* Not every object needs every service, support the minimum overhead solution as much as possible.
* Some services might need to target .NET Framework, not .NET Standard, where the support does not exist: but this should be "ok" - if you want to use a service that relies on .NET Framework, you simply need to implement your solution there (or in a client-server scenario, you can implement a different set of services in each tier, if you like!)
* A "collection container of entities" should exist and provide an observable, concrete common generic framework collection; it's EntitySet&lt;T&gt; and implements IEnumerable&lt;T&gt;, ICollection&lt;T&gt; and IList&lt;T&gt;.
* Services can include:
	* UI data-binding support (e.g. implements INotifyPropertyChanged, etc.) for entities and collections of entities.
	* Caching (ability to plug in to really *any* kind of caching scheme - I like my existing DB-backed in-memory object cache but plenty of others) - again, caching is a *service*, how you cache can be based on a *provider* model. Also, caching needs can vary by object (e.g. static tables you might cache for much longer than others).
	* Key management (manages concept of identity, uniqueness, key generation (SEQUENCE / IDENTITY / Guids), surrogate keys, cascading operations, etc.).
	* In-memory indexing, sorting, filtering (LINQ to Objects is a given, but this is a way to make that more efficient especially when dealing with large objects).
	* Validations (rather than decorate the POCO with these, keep them separate - perhaps some validations are UI-centric, others are not; leave it to the framework user to decide how much or little they want).
	* Extended properties: for example, being able to retrieve from a procedure that returns extra details not strictly part of the object - useful for binding and ultimately may match a saveable object (i.e. not a completely generic bag). Ideally strong-type these additional fields for intellisense, performance, type safety, effective DB contract, etc. These by being kept separate and managed via a service means it's completely opt-in and we don't carry unneeded baggage. This is different from the ability to load sets of existing objects (e.g. Person) from arbitrary procedures as well: if the result set matches the shape of the object, that should just work out of the box with no special effort.
	* Persistence and change tracking (PCT) - can identify "original values", "row states", and enough detail to serialize "differences" across process boundaries.
	* Audit - manages "last updated" fields, logical deletion, etc. Things like Last Updated fields should have framework support so we don't have to worry about managing these beyond high level settings.
	* Database Persistence:
		* Supports stored procedure mapping such that can leverage existing SQL audit templates that do a good job of building a CRUD layer, optimistic concurrency and temporal tables "done right" (i.e. determine *who* deleted a record!! - not something you can do natively with SQL2016 temporal tables).
		* Supports parameter mapping such that can still read and write from objects that are strictly "proc-based", where needed. (I.e. having tables behind it is not necessary).
		* Understands connection management / transaction participation, etc. - see below.
		* The full .Save() type of functionality for single records, isolated collections and complete object graphs.
		* Support some of the nice features of CodeXFramework V1.0 including the option to bulk insert added rows, etc.
* Should be able to work with "generic data". A fair example is a DataTable - but we can offer lighter options (both in code simplicity and with performance).
* The service architecture should be "pluggable" so others can create services that plug in easily. (Core services vs. non-core.)
* The life-time of services may not be a simple "using block" - e.g. UI services could last for the life of a form - give control to the developer with options. Plus connection management should be split into a different service since sometimes this needs to be managed independently (i.e. multiple connections in a single service scope, or crossing service scopes).
* Detection of changes should be easy and allow UI's to be informed about dirty state changes.
* Supports proper round-tripping of values (keys assigned in DB on save show up in memory, audit dates/names assigned show up as well, etc.)
* Database access: focus is on using stored procedures. Why? Reasons:
	* The "extra layer" is something I've exploited very successfully in the past - it's an interception layer that's at least "there".
	* The layer is trivial to generate for CRUD, so difficulty is hard to justify (you would normally only write custom retrievals which can stand to have optimization that's easier centralized within procedures anyhow).
	* The layer can value-add - optimistic concurrency, audit, etc. - controlled via declarative settings.
	* There are some interesting optimization opportunites: native compiled procs, for example. Combine that with in-memory tables where it makes sense, and - just wow.
	* The layer can support non-tabular entities - e.g. flattened procedures that interact with multiple tables, or cross-db situations, etc.
	* This is just one flavor of database access - the provider interface means it'd be possible to integrate other ways, perhaps even a LINQ to SQL layer (a roadmap item).
* Databases - need to support the possibility of different objects being sourced/saved to different databases and/or servers and/or schemas. (Needs control type-by-type in registration process.)
* Use parallelism where possible.
* Registration process should support multiple code gens - for example you could generate 3 different registration methods for 3 different databases.
* All of the setup code we see should be generated from models: be it a database, or even an ERD. The end goal is you identify some logical model you want to work with, map it (visually, ideally), and then plumbing is created for you - start using objects and away you go.
* Sensitive to your preferences: for example, if your standard is to use integer primary keys versus longs, we should let you do that easily. The same is true with your naming conventions (although name mapping is more of a vnext feature).

## General Architecture
The general apporach of CodexMicroORM is to *wrap* objects - typically your POCO. Why? We make no assumptions about what services your POCO may provide. In order to have an effective framework, one handy interface we want to leverage is INotifyPropertyChanged. If your POCO do not implement this, we use a wrapper that *does* and use *containment* with your POCO - wrapping it.

The concept of a *context* isn't unfamiliar to many ORM frameworks. CodexMicroORM also employes the idea of a context, but is called the *service scope*. A service scope is fairly unobtrusive: it doesn't need to be generated or shaped in a particular way - it can have *any* object added to it, becoming "tracked."

There can be two kinds of wrappers: your own custom wrapper class that typically inherits from some other POCO and adds "framework-friendly functionality," and "infrastructure wrappers" that are more like DataTable's, except they derive from DynamicObject. The framework can use multiple flavors of these objects:

![Wrapper architecture](http://www.xskrape.com/images/cef_wrapper_arch.jpg)

The key is flexibility: you can mix-and-match approaches even within a single app! In fact our demo app does this by using a Person POCO, along with a PersonWrapped wrapper (theoretically code generated). The Phone POCO does not have a corresponding PhoneWrapper, but that's okay - we don't need it since the infrastructure wrapper used will be provisioned with more services than it would be if you had a wrapper (or POCO) that say implemented INotifyPropertyChanged.

In release 0.2, the main way to interact with the database is using stored procedures. As mentioned in the design goals, this is largely intentional, but it *is* just one way to implement data access, which is done using a provider model.

Use of stored procedures as the "data layer" is by convention: you would typically name your procedures up_[ClassName]_i (for insert), up_[ClassName]_u (for update), up_[ClassName]_d (for delete), up_[ClassName]_ByKey (retrieve by primary key). (The naming structure can be overridden based on your preferences.) Beyond CRUD, you can craft your own retrieval procedures that may map to existing class layouts - or identify completely new formats. The sample app is a good place to start in understanding what's possible!

## Sample / Testing App
I've included both a sample app (WPF) and a test project. The sample app illustrates UI data binding with framework support, along with a series of scenarios that both exercise and illustrate. We can see for example with this class:

```c#
public class Person
{
	public int PersonID { get; set; }
	public string Name { get; set; }
	public int Age { get; set; }
	public string Gender { get; set; }
	public IList<Person> Kids { get; set; }
	...
```

"Kids" is an object model concept where in the database this is implemented through a ParentPersonID self-reference on the Person table. We should be able to do this:

```c#
sally.Kids.Remove(zella);
CEF.DBSave();
```

... and expect that Zella's ParentPersonID will be nullified when it's saved by the call to DBSave(). Most of this magic happens because we've established key relationships early in the app life-cycle:

```c#
// Establish primary keys based on types (notice for Phone we don't care about the PhoneID in the object model - do in the database but mCEF handles that!)
KeyService.RegisterKey<Person>(nameof(Person.PersonID));
KeyService.RegisterKey<Phone>("PhoneID");

// Establish all the relationships we know about via database and/or object model
KeyService.RegisterRelationship<Person>(TypeChildRelationship.Create<Person>("ParentPersonID").MapsToChildProperty(nameof(Person.Kids)));
KeyService.RegisterRelationship<Person>(TypeChildRelationship.Create<Phone>().MapsToParentProperty(nameof(Phone.Owner)).MapsToChildProperty(nameof(Person.Phones)));
```

This code is something that we can ideally eventually generate, as opposed to writing it by hand.

In terms of the SQL to support these examples: a .sql script is included in both the test and demo projects. This script will create a new database called CodexMicroORMTest that includes all the necessary schema objects to support the examples. You may need to adjust the DB_SERVER constant to match your own SQL Server instance name.

The SQL that's included is a combination of hand-written stored procedures and code generated objects, including procedures (CRUD) and triggers. The code generator I've used is SQL-Hero, but you can use whatever tool you like. The generated SQL is based on declarative settings that identify: a) what type of opimistic concurrency you need (if any), b) what kind of audit history you need (if any).

Of note, the audit history template used here has an advantage over temporal tables found in SQL 2016: you can identify *who* deleted records, which (unfortunately) can be quite useful! CodexMicroORM plays well with the data layer, providing support for LastUpdatedBy, LastUpdatedDate, and IsDeleted (logical delete) fields in the database.

## Release Notes
* 0.2 - December 2017 - Initial Release (binaries available on [NuGet - Core](https://www.nuget.org/packages/CodexMicroORM.Core/), [Nuget - Bindings](https://www.nuget.org/packages/CodexMicroORM.BindingSupport/))

## Roadmap / Plans
Release 0.2 covers some basic scenarios. For 0.3 I'd like to add:

* Validation services
* Name translation services
* Serializing changes / rehydrating objects
* Support for more types of cardinalities, object mapping
* Some initial code-gen support

Clearly tool support such as for code generation could prove *very* useful - watch for that offered as "add-on" products and likely offered initially through SQL-Hero given that some existing templates can likely be tweaked to get a quick win for CodexMicroORM.

Have opinions about what you'd like to see? Drop me a line @ joelc@codexframework.com.
