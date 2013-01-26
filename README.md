Lorg
====

.NET 4.5 Exception Logging framework using content-addressable hashing to reduce storage requirements
and increase searchability / ease of filtering.

Status
======
Still a work in progress, but core logging functionality is implemented.

Key Goals
=========
 * Asynchronous execution
   * Exceptions are logged in background tasks
   * All SQL writes are concurrent; virtually no FK write dependencies due to content-addressable storage
 * Content-addressable records for efficient storage and searchability / ease of filtering
 * Fast execution to remain as transparent as possible to the host application
 * Non-redundant storage of all data in MS SQL Server (2008 R2 and up)
 * Simple to integrate into existing applications
 * Support for MVC 4
 * Configurable logging policy per exception type
 * Triage support for defects related to exception reports; record external system tracking ID
