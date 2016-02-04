// ----------------------------------------------------------------------------
//  Licensed to FlexSearch under one or more contributor license 
//  agreements. See the NOTICE file distributed with this work 
//  for additional information regarding copyright ownership. 
//
//  This source code is subject to terms and conditions of the 
//  Apache License, Version 2.0. A copy of the license can be 
//  found in the License.txt file at the root of this distribution. 
//  You may also obtain a copy of the License at:
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
//  By using this source code in any fashion, you are agreeing
//  to be bound by the terms of the Apache License, Version 2.0.
//
//  You must not remove this notice, or any other, from this software.
// ----------------------------------------------------------------------------
namespace FlexSearch.SqlConnector

open FlexSearch.Api.Constants
open FlexSearch.Api.Model
open FlexSearch.Core
open System
open System.Data
open System.Data.SqlClient

// ----------------------- IMPORTANT NOTE ---------------------------------
// In this case, since the SQL connector is part of the Core project, its
// SqlIndexingRequest object is already built into the swagger definition,
// thus is part of the FlexSearch.Api module.
// If you're developing your own connector/plugin, then define your 
// request object here.
// ------------------------------------------------------------------------

[<Sealed>]
[<Name("POST-/indices/:id/sql")>]
type SqlHandler(queueService : IQueueService, jobService : IJobService) = 
    inherit HttpHandlerBase<SqlIndexingRequest, string>()
    
    let ExecuteSql(request : SqlIndexingRequest, jobId) = 
        let isNotBlank = isBlank >> not
        if request.CreateJob then 
            let job = new Job(JobId = jobId, JobStatus = JobStatus.InProgress)
            jobService.UpdateJob(job) |> ignore
        try 
            use connection = new SqlConnection(request.ConnectionString)
            use command = new SqlCommand(request.Query, Connection = connection, CommandType = CommandType.Text)
            command.CommandTimeout <- 300
            connection.Open()
            let mutable rows = 0
            use reader = command.ExecuteReader()
            if reader.HasRows then 
                while reader.Read() do
                    let document = new Document(IndexName = request.IndexName, Id = reader.[0].ToString())
                    for i = 1 to reader.FieldCount - 1 do
                        document.Fields.Add(reader.GetName(i), reader.GetValue(i).ToString())
                    if request.ForceCreate then queueService.AddDocumentQueue(document)
                    else queueService.AddOrUpdateDocumentQueue(document)
                    rows <- rows + 1
                    if rows % 5000 = 0 then jobService.UpdateJob(jobId, JobStatus.InProgress, rows)
                jobService.UpdateJob(jobId, JobStatus.Completed, rows)
                Logger.Log
                    (sprintf "SQL connector: Job Finished. Query:{%s}. Index:{%s}" request.Query request.IndexName, 
                     MessageKeyword.Plugin, MessageLevel.Verbose)
            else 
                jobService.UpdateJob(jobId, JobStatus.CompletedWithErrors, rows, "No rows returned.")
                Logger.Log
                    (sprintf "SQL connector error. No rows returned. Query:{%s}" request.Query, MessageKeyword.Plugin, 
                     MessageLevel.Error)
        with e -> 
            jobService.UpdateJob(jobId, JobStatus.CompletedWithErrors, 0, (e |> exceptionPrinter))
            Logger.Log
                (sprintf "SQL connector error: %s" (e |> exceptionPrinter), MessageKeyword.Plugin, MessageLevel.Error)
    
    let bulkRequestProcessor = 
        MailboxProcessor.Start(fun inbox -> 
            let rec loop() = 
                async { 
                    let! (body, jobId) = inbox.Receive()
                    ExecuteSql(body, jobId)
                    return! loop()
                }
            loop())
    
    let processRequest index (body : SqlIndexingRequest) = 
        let createJob() = 
            if body.CreateJob then 
                let guid = Guid.NewGuid()
                bulkRequestProcessor.Post(body, guid.ToString())
                guid.ToString()
            else 
                ExecuteSql(body, "")
                ""
            |> ok
        body.IndexName <- index
        createJob()
    
    override __.Process(request, body) = SomeResponse(processRequest request.ResId.Value body.Value, Ok, BadRequest)
