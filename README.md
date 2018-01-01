# CodexMicroORM
An alternative to ORM's such as Entity Framework, offers database mapping for your existing CLR objects with minimal effort. CodexMicroORM excels at performance and flexibility as we explain further below.

## Background
Why build a new ORM framework? After all, Entity Framework, nHibernate and plenty of others exist and are mature. Speaking of "mature," I built CodeXFramework V1.0 several years ago and it shares some of the complaints I've seen some people make about many mainstream ORM's: they can be "heavy," "bloated" and as much as we'd like them to be "unobtrusive" - sometimes they *are*.

Wouldn't it be nice if we could simply use our existing POCO (plain-old C# objects) and have them become *ORM-aware*? That's the ultimate design goal of CodexMicroORM: to give a similar vibe to what we got with "LINQ to Objects" several years ago. (Recall: that turned anything that was IEnumerable&lt;T&gt; into a fully LINQ-enabled list source - which opened up a whole new world of possibility!)

CodexMicroORM isn't necessarily going to do everything that other, larger ORM frameworks can do - that's the "micro" aspect. We'll leave some work to the framework user, favoring performance much of the time. That said, we do aim for *simplicity* as I hope the demo application illustrates. As one example, we can create a sample Person record in *one line of code*:

```c#
CEF.NewObject(new Person() { Name = "Bobby Tables", Age = 7, Gender = "M" }).DBSave();
```

The demo project shows several other non-trivial use cases - and more will follow. This initial 0.2 release of CodexMicroORM (aka "CEF" or "Codex Entity Framework") is young enough that most core interfaces should stay the same, but we could be making some breaking changes as refactoring dictates.

I've added a section below that compares some popular ORM frameworks. It's evident that performance was high on my mind - not to give away the punch-line, but CodexMicroORM soundly *beats* Entity Framework on some comparable sample operations (with very similar syntax).

## Design Goals
Diving more deeply, what does CodexMicroORM try to do *better* than other frameworks? What are the guiding principles that have influenced it to date?

* Support .NET Standard 2.0 as much as possible. (ICustomTypeProvider as one example isn't there, so for WPF we have the CodexMicroORM.BindingSupport project which is net461.)
* Entities can be *any* POCO object, period.
* Entities do not need to have any special "database-centric" properties (if they don't want - many will want them). In our example model, note that Phone doesn't have a PhoneID as one example.
* Entities should be able to carry "extra" details that come from the database (aka "extended properties") - but these should be "non-obtrusive" against the bare POCO objects being used.
* Entities can be nested in what would be considered a normal object model (e.g. "IList&lt;Person&gt; Children" as a property on Person vs. a nullable ParentID which is a database/storage concept - but the DB way should be supported too!).
* Use convention-based approaches - but codify these conventions as global settings that describe how you prefer to work (e.g. do you not care about saving a "Last Updated By" field on records? The framework should support it, but you should be able to opt out with one line of code at startup).
* Entities can be code-gened from databases, but is *not required at all*.
* Code-gen properties by entity / database object should be retrievable from a logical model (e.g. Erwin document), SQL-Hero (repository supports flexible user-defined properties), or some other type of file-based storage.
* Entities shouldn't have to be *contained* by "contexts" like you see in EF: they really can be *any* object graph you like! This might seem like a small deal, but it really frees you up to work with objects however you like, with some conventions-based rules.
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
* The life-time of services may not be a simple "using block" - e.g. UI services could last for the life of a form - give control to the developer with options. Plus, connection management should be split into a different service since sometimes this needs to be managed independently (i.e. multiple connections in a single service scope, or crossing service scopes).
* Detection of changes should be easy and allow UI's to be informed about dirty state changes.
* Supports proper round-tripping of values (keys assigned in DB on save show up in memory, audit dates/names assigned show up as well, etc.)
* Database access: focus is on using stored procedures. Why? Reasons:
	* The "extra layer" is something I've exploited very successfully in the past - it's an interception layer that's at least "there".
	* The layer is trivial to generate for CRUD, so difficulty is hard to justify (you would normally only write custom retrievals which can stand to have optimization that's easier centralized within procedures anyhow).
	* The layer can value-add - optimistic concurrency, audit, etc. - controlled via declarative settings.
	* There are some interesting optimization opportunities: native compiled procs, for example. Combine that with in-memory tables where it makes sense, and - just wow.
	* The layer can support non-tabular entities - e.g. flattened procedures that interact with multiple tables, or cross-db situations, etc.
	* This is just one flavor of database access - the provider interface means it'd be possible to integrate other ways, perhaps even a LINQ to SQL layer (a roadmap item).
* Databases - need to support the possibility of different objects being sourced/saved to different databases and/or servers and/or schemas. (Needs control type-by-type in registration process.)
* Use parallelism where possible, and make it as thread-safe as possible for framework users.
* As a rule, the framework will trade higher memory use for better performance. We do have some data structures that can appear to hog memory, but I've taken the time to ensure that when service scopes are disposed, memory is released as expected. Benchmarking also shows a *linear* performance characteristic, whereas EF is *non-linear*.
* Expect fewer "sanity checks" than you might get out of some ORM's - again, favoring performance heavily. As one example, we're going to trust you to do things like enable MultipleActiveResultSets on your connections when doing parallel operations (although in this example, we do check for this for some internal parallel operations). Again, the "micro" means light-weight, within reason.
* Registration process should support multiple code gens - for example you could generate 3 different registration methods for 3 different databases.
* All of the setup code we see should be generated from models: be it a database, or even an ERD. The end goal is you identify some logical model you want to work with, map it (visually, ideally), and then plumbing is created for you - start using objects and away you go.
* Sensitive to your preferences: for example, if your standard is to use integer primary keys versus longs, we should let you do that easily. The same is true with your naming conventions (although name mapping is more of a vnext feature).

## General Architecture
The general approach of CodexMicroORM is to *wrap* objects - typically your POCO. Why? We make no assumptions about what services your POCO may provide. In order to have an effective framework, one handy interface we want to leverage is INotifyPropertyChanged. If your POCO do not implement this, we use a wrapper that *does* and use *containment* with your POCO - wrapping it.

The concept of a *context* isn't unfamiliar to many ORM frameworks. CodexMicroORM also uses the idea of a context, but is called the *service scope*. A service scope is unobtrusive: it doesn't need to be generated or shaped in a particular way - it can have *any* object added to it, becoming "tracked."

There can be two kinds of wrappers: your own custom wrapper class that typically inherits from some other POCO and adds "framework-friendly functionality," and "infrastructure wrappers" that are more like DataTable's, except they derive from DynamicObject. The framework can use multiple flavors of these objects:

![Wrapper architecture](http://www.xskrape.com/images/cef_wrapper_arch.jpg)

The key is flexibility: you can mix-and-match approaches even within a single app! In fact, our demo app does this by using a Person POCO, along with a PersonWrapped wrapper (theoretically code generated). The Phone POCO does not have a corresponding PhoneWrapper, but that's okay - we don't need it since the infrastructure wrapper used will be provisioned with more services than it would be if you had a wrapper (or POCO) that say implemented INotifyPropertyChanged.

In release 0.2, the main way to interact with the database is using stored procedures. As mentioned in the design goals, this is largely intentional, but it *is* just one way to implement data access, which is done using a provider model.

Use of stored procedures as the "data layer" is by convention: you would typically name your procedures up\_[ClassName]\_i (for insert), up\_[ClassName]\_u (for update), up\_[ClassName]\_d (for delete), up\_[ClassName]\_ByKey (retrieve by primary key). (The naming structure can be overridden based on your preferences.) Beyond CRUD, you can craft your own retrieval procedures that may map to existing class layouts - or identify completely new formats. The sample app is a good place to start in understanding what's possible!

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

In terms of the SQL to support these examples: a .sql script is included in both the test and demo projects. This script will create a new database called CodexMicroORMTest that includes all the necessary schema objects to support the examples. You may need to adjust the DB\_SERVER constant to match your own SQL Server instance name.

The SQL that's included is a combination of hand-written stored procedures and code generated objects, including procedures (CRUD) and triggers. The code generator I've used is SQL-Hero, but you can use whatever tool you like. The generated SQL is based on declarative settings that identify: a) what type of optimistic concurrency you need (if any), b) what kind of audit history you need (if any).

Of note, the audit history template used here has an advantage over temporal tables found in SQL 2016: you can identify *who* deleted records, which (unfortunately) can be quite useful! CodexMicroORM plays well with the data layer, providing support for LastUpdatedBy, LastUpdatedDate, and IsDeleted (logical delete) fields in the database.

Is SQL-Hero the primary means by which I plan to offer "value add" for the framework (such as code generation)? Maybe, maybe not. I do have a SQL-Hero template enhancement that will be released in Q1 2018 that offers a *40% CRUD performance improvement* for SQL audit (history queries are slower but also used much less often in practice). However, keep watch for new tool support as a Visual Studio extension which can merge metadata from both database and existing CLR objects to create effective wrappers and all of your initialization logic.

## Performance Benchmarks
In release 0.2.1, I've enhanced the WPF demo to include a benchmark suite that tests CodexMicroORM, Entity Framework, nHibernate and Dapper. I selected these other frameworks on the expectation that they've "worked out the kinks" and we should be able to judge both performance and maintainability (code size).

The methodology for testing is to construct two types of test (benchmarks 1 and 2) that can be replicated in all four frameworks. Benchmark 1 involves creating new records over multiple parent-child relationships. Benchmark 2 involves loading a set of people based on some criteria, updating them and saving them (and in some cases, their Phone data).

I assume the goal output for all frameworks is to populate and save *object models* that might end up being passed to different components for further processing. This implies we should have all system-assigned values present in memory by the time each test finishes. I also tried to implement the fastest solution possible by introducing parallelism where a) it improved performance, b) the framework allowed it without returning errors. I also chose solutions that resulted in the least possible "tweaking" - mostly out-of-the-box settings or patterns that might seem obvious to a relative framework newbie. I feel this is fair since if you're an expert in any of these frameworks, chances are you won't be comparing them: you'll be using the one you know best and are likely to self-adjust your coding style based on what you know works best. (Whether that's good or not for say maintainability - that's open for debate.)

For measuring code size and performance, I omit code that can be considered "initialization," "start-up only," and "code generated or ideally code generated." This includes virtually all of the SQL objects in our sample database, except for a couple of hand-written procedures (e.g. up_Person_SummaryForParents). I've excluded the size of up_Person_SummaryForParents from the code stats mainly because it can be considered general-purpose and usable by all frameworks, if needed. (For the record, it's 838 characters.)

Here are execution times, per row, for the various frameworks:

![ORM Framework Performance Comparison](http://www.xskrape.com/images/cef_compare1.png)

These numbers combine two tests: one with 3000 rows and one with 6000 rows. "Existing" is the case where we take an existing, pre-populated object model and try to save it, in its entirety, based on the ORM's ability to understand whether rows are added, modified or deleted. "Save Per" is a modified version where we end with the same set of rows in the database, but we're committing changes "per parent entity." I had originally intended to just test "existing" but it became evident that there's a substantial difference in the styles between frameworks. (You can use this to inform how to use these frameworks and get some understanding of their inner workings.)

Here are execution times, split based on whether we try to save 3000 or 6000 initial parent entity rows:

![ORM Framework Performance Comparison - Scalability](http://www.xskrape.com/images/cef_compare2.png)

Frameworks that don't keep their performance characteristic steady when increasing the data set size are *non-linear* - performance is *worse* than O(n).

Another metric is how much code we need to write to implement the scenario, shown here:

![ORM Framework Complexity Comparison](http://www.xskrape.com/images/cef_compare3.png)

The higher the character count, the more code that was needed - a logical assumption is the complexity is *higher* and the maintainability is *lower*.

Raw performance data is available [here](https://codexenterprisesllc-my.sharepoint.com/personal/sqlheroguy_codexenterprisesllc_onmicrosoft_com/_layouts/15/guestaccess.aspx?docid=0b654147859314d11b3d8c1cce8cfd4a2&authkey=Ad7gBci8B5HkHlwpBpE-ouw&e=9a0b84196e0e4fe18c56fbeddf9002ce).

### Entity Framework Results
The biggest concern I have with the EF results is that they're *non-linear*. Compare performance per row between the 3000 and 6000 row cases: the 6000 row case has nearly *doubled* the per row time, regardless of approach! This is not a good story for scalability.

The EF appologist might say: "you shouldn't be using it in the way you're testing it here - it's not intended for *that*, use a different choice like Dapper if you need extreme performance or are dealing with many rows." I'd respond by saying the purpose of the comparison is to see if there *does* exist a general-purpose ORM framework that has linear performance in what amounts to randomly selected scenarios. Dapper succeeds (but as I discuss below, it has its own down-sides), and so does CodexMicroORM - but both EF and nHibernate fail the test in at least one scenario.

I'm willing to be told "but you could have done things differently with framework X and it would have given much better results" - but again, the most obvious approach should be measured since if we require "deep knowledge" of the framework, chances are good we'll be cutting "bad code" for a while, until we've learned its ins-and-outs. If we could achieve good results with *intuition*, that would be ideal, of course.

Generally speaking, EF is not thread-safe and trying to introduce Parallel operations resulted in various errors such as "The underlying provider failed on Open" and "Object reference not set to an instance of an object", deep within the framework. There was also an example of needing some "black box knowledge" where I had to apply a .ToList() on an enumerator, otherwise it would result in an obscure error. I'm not a fan of this "it *doesn't* just work," although we could argue it's a minor inconvenience and most frameworks have some level of "black magic" required.

We might give extra marks to EF6 in its ability to properly interpret our LINQ expression in Benchmark 2 and thereby avoid use of the general-purpose stored procedure, up_Person_SummaryForParents. However, we might not always be so lucky: using procedures will make sense in situations where we need to use temp tables or use other SQL coding constructs to squeeze out good performance.

### nHibernate Results
Let's face it: nHibernate must deal in *stylized* objects, not necessarily true POCO that might pre-exist in your apps. The clearest evidence is the fact all properties must be *virtual* - you may or may not have implemented your existing POCO's with that trait.

Something you might notice in the code is the nHibernate solution includes quite a bit of set-up code. I chose to use the Fluent nHibernate API, but the XML approach would have resulted in quite a bit of configuration as well. I also spent a bit of time trying to understand the ins-and-outs of the correct way to do the self-referencing relationship on Person, and borrowed the generated EF classes which match the database structure completely.

On the performance side, nHibernate scales linearly with the "save existing object model" scenario - but it does *not* for the "save per parent entity" case. (This would also match a case where we chose to use database transactions to commit per parent entity.) Like EF, parallelism was not kind to nHibernate and I had to avoid it generally. Another minor consideration is the fact as of today (12/31/17), nHibernate does not support NetStandard 2.0.

### Dapper Results
It became evident as I tried to implement the Dapper solution that it's not really the *same* style or purpose as the other ORM tools. I'd call it more of a "data access helper library" instead. Unlike the other frameworks tested, it does *not track row state*: it's up to you as the developer to know when to apply insert, update or delete operations against the database. This helps it achieve its excellent performance, but it's just a thin wrapper for raw ADO.Net calls.

Because of this, I didn't attempt a "populate and save all" version of tests for Dapper: it only really made sense to do the "save per parent entity" method. If we constructed a way to do a generalized "save" using Dapper, chances are good we'd lose the extreme performance benefits since we'd be applying the principles that make other frameworks slower.

Dapper although the clear winner on performance was the clear loser on code size / maintainability. Anecdotally, I also had more run-time errors to debug than other framework examples since there's no strong typing. Similarly, changes in the database schema are more likely to cause run-time errors for the same reason: no strong typing we could tie back to code generation. (Although I'm sure if I looked hard enough, someone has created templates to act as wrappers for database objects.) This in turn speaks to its nature, much as EF and nHibernate, being more highly-coupled with your database than you might want to believe (e.g. requiring ID's to support relationships, etc.).

### CodexMicroORM Results
CEF offers linear performance results and has among the smallest, most succinct code implementations. It's worth noting that you can use EF-generated classes, if you like, with CEF.

I did an extra test for CEF that I did not include for other frameworks - because it's somewhat unique to CEF out-of-the-box - the ability to save added data using BULK INSERT. That case for Benchmark 1 yielded a median time of 0.9 ms/row (both 3000 and 6000 row runs). This is the fastest way to "save an existing, full object graph" among all tests. The trade-off? You don't get PhoneID's assigned in memory at the end of saving.

Am I claiming that CEF is "perfect"? Certainly not: this is version 0.2.1 - but I'm quite confident it's positioned well to offer O(n) performance based on data set size. If you discover something slow, let me know!

Finally, I wanted to verify that CEF has no memory leaks as part of the demo program. I did this using a memory monitoring tool, issuing a snapshot / garbage collect after all CEF tests were complete:

![ORM Framework Complexity Comparison](http://www.xskrape.com/images/cef_cef_memory_postbenchmark.png)

The orange line is allocated memory - within the context of the red box, this is where a collect had been issued and the orange line has dropped to near-zero. The Type Details shows us what's the largest "left over" allocated objects: mainly ADO.Net static data that we have no control over.

### Conclusions
* Entity Framework and nHibernate forced me to adopt class structures that match the database more closely than my original POCO's do. For example, my "Phone" POCO has no PhoneID, nor should it really require it from an object model perspective: it's a storage construct (an important one, yes, but strictly speaking, it should not be forced on our object model, if we don't want it).
* Entity Framework and nHibernate both had at least one non-linear performance scenario.
* Dapper, although very fast, provided the most verbose implementation by a factor of 2x-3x.
* None of the frameworks tested (other than CodexMicroORM) natively understand the concept of "last updated by", "last updated date" fields as standardized audit fields. This results in more repetitive code.
* The pattern of "saving while populating" seems to work better than "save everything at the end" with some frameworks - but this may or may not align with your needs in each situation. For example, you may be populating an object model, passing it around, and saving it after some additional work - this would not align with "save as populate" and if we're looking for the best *generalized* solution, ideally it balances performance while offering different options to solve your problems.
* I'm probably biased, but CodexMicroORM shows a good balance of "small code" and good, linear performance over all scenarios presented. These aren't fake metrics - you're free to try them out yourself in the demo project, and improve on them, even!

## Release Notes
* 0.2.0 - December 2017 - Initial Release (binaries available on [NuGet - Core](https://www.nuget.org/packages/CodexMicroORM.Core/), [Nuget - Bindings](https://www.nuget.org/packages/CodexMicroORM.BindingSupport/))
* 0.2.1 - December 2017
	* remove all use of Reflection GetValue/SetValue (performance)
	* "preferred type" detection on retrieval
	* optional GetLastUpdatedBy delegate on session scope settings
	* "performance and memory fixes"
	* initial benchmarks
	* other minor adjustments

## Roadmap / Plans
Release 0.2 covers some basic scenarios. For 0.3 I'd like to add:

* Validation services
* Name translation services
* Serializing changes / rehydrating objects
* Support for more types of cardinalities, object mapping
* Some initial code-gen support
* Add more collection types (EntityHashSet, EntityDictionary)

Clearly tool support such as for code generation could prove *very* useful - watch for that offered as "add-on" products and likely offered initially through SQL-Hero given that some existing templates can likely be tweaked to get a quick win for CodexMicroORM.

Have opinions about what you'd like to see? Drop me a line @ joelc@codexframework.com.
