
SELECT TOP 1000
      i.exInstanceID, i.LoggedTimeUTC, i.SequenceNumber, i.IsHandled, i.Message, i.ApplicationName, i.EnvironmentName, i.ExecutingAssemblyName
    , e.TypeName + ', ' + e.AssemblyName AS ExceptionType, e.StackTrace
    , webapp.MachineName, webapp.ApplicationID, webapp.SiteName, webapp.PhysicalPath, webapp.VirtualPath
    , web.HttpVerb
    , requ.Scheme + '://' + requ.HostName + ':' + CONVERT(varchar(5), requ.PortNumber) + requ.AbsolutePath + requq.QueryString AS RequestURL
    , web.RequestHeadersCollectionID
    , web.AuthenticatedUserName
FROM [dbo].[exInstance] i
JOIN [dbo].[exException] e ON e.exExceptionID = i.exExceptionID
LEFT JOIN [dbo].[exContextWeb] web ON web.exInstanceID = i.exInstanceID
LEFT JOIN [dbo].[exURLQuery] requq ON requq.exURLQueryID = web.RequestURLQueryID
LEFT JOIN [dbo].[exURL] requ ON requ.exURLID = requq.exURLID
LEFT JOIN [dbo].[exWebApplication] webapp ON webapp.exWebApplicationID = web.exWebApplicationID
ORDER BY i.[LoggedTimeUTC] DESC, i.[SequenceNumber] ASC

-- Get header collections
SELECT TOP 1000
    kv.[exCollectionID]
   ,kv.[Name]
   ,v.[Value]
   ,kv.[exCollectionValueID]
FROM [dbo].[exCollectionKeyValue] kv
JOIN [dbo].[exCollectionValue] v ON v.[exCollectionValueID] = kv.[exCollectionValueID]
ORDER BY kv.[exCollectionID]
