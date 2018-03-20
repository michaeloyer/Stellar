﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Stellar.Documents;
using Stellar.Serialization;

namespace Stellar
{
    internal class DocumentsManager : IDocuments
    {
        private readonly Uri _uri;
        private readonly string _apiKey;
        private readonly string _dbName;
        private readonly string _collectionName;
        private readonly ISerializer _serializer;
        private readonly string _basePath;
        private readonly QueryProvider _queryProvider;

        public DocumentsManager(Uri uri, string apiKey, string dbName, string collectionName, ISerializer serializer)
        {
            _uri = uri;
            _apiKey = apiKey;
            _collectionName = collectionName;
            _dbName = dbName;
            _serializer = serializer;
            _basePath = $"dbs/{dbName}/colls/{_collectionName}/docs";
            _queryProvider = new CosmosQuery(uri, apiKey, _basePath, _serializer);
        }

        public async Task<CosmosHttpResponse> Delete(string id)
        {
            var queryPath = $"{_basePath}/{id}";
            var response = await GetResourceResult("delete", queryPath, queryPath);
            return response;
        }

        public async Task<T> Get<T>(string id) where T : class
        {
            var queryPath = $"{_basePath}/{id}";
            var response = await GetResourceResult("get", queryPath, queryPath);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            return _serializer.Deserailize<T>(response.Body);
        }

        /// <summary>
        /// Query Cosmos DB with ad-hoc SQL
        /// </summary>
        /// <remarks>
        /// This may eventually go away. The goal is to make a LINQ to Cosmos SQL API. However there
        /// will always be edge cases.
        /// </remarks>
        /// <typeparam name="T">Source and Response type</typeparam>
        /// <param name="sql">Cosmos SQL query</param>
        /// <param name="param">SQL Parameters</param>
        /// <returns></returns>
        public async Task<IEnumerable<T>> Query<T>(string sql, object param = null) => await Query<T, T>(sql, param);

        /// <summary>
        /// Query Cosmos DB with ad-hoc SQL
        /// </summary>
        /// <remarks>
        /// This may eventually go away. The goal is to make a LINQ to Cosmos SQL API. However there
        /// will always be edge cases.
        /// </remarks>
        /// <typeparam name="T1">Source type</typeparam>
        /// <typeparam name="T2">Response type</typeparam>
        /// <param name="sql">Cosmos SQL query</param>
        /// <param name="param">SQL Parameters</param>
        /// <returns></returns>
        public async Task<IEnumerable<T2>> Query<T1, T2>(string sql, object param = null)
        {
            sql = InjectSqlTypeClause(sql, typeof(T1));
            var resourceValue = _basePath.Substring(0, _basePath.LastIndexOf('/'));
            var query = CreateCosmosQueryJson(sql, param);
            var response = await GetResourceResult("post", _basePath, resourceValue, query, jsonQuery: true);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new CosmosQueryException(response.Body);

            var cosmosQueryResponse = _serializer.Deserailize<CosmosQueryResponse>(response.Body);
            if (cosmosQueryResponse._count == 0)
                return Enumerable.Empty<T2>();
            return _serializer.Deserailize<List<T2>>(cosmosQueryResponse.Documents.ToString());
        }

        private string InjectSqlTypeClause(string sql, Type type)
        {
            var regex = new Regex(@"\s+from\s+(?'collectionIdentifier'\w+)($|\s+)", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(10));
            var match = regex.Match(sql);
            if (!match.Groups["collectionIdentifier"].Success)
                throw new FormatException("Unable to understand SQL query. Ensure that it has one and only one FROM statement.");
            var collectionIdentifier = match.Groups["collectionIdentifier"].Value;

            var typeClause = "(" + collectionIdentifier + "._type = '" + type.FullName + "')";

            regex = new Regex(@"\s+where\s+(?'clause'(.|\s)+?)(Order|$)", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(10));
            match = regex.Match(sql);
            if (match.Groups["clause"].Success)
            {
                sql = sql.Insert(match.Groups["clause"].Index + match.Groups["clause"].Length, ") ")
                .Insert(match.Groups["clause"].Index, typeClause + " AND (");
            }
            else
            {
                regex = new Regex(@"\s+order\s+by\s+.*", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(10));
                match = regex.Match(sql);
                if (match.Success)
                    sql = sql.Insert(match.Index, " WHERE " + typeClause + " ");
                else
                    sql += " WHERE " + typeClause;
            }

            return sql;
        }

        public CosmosQueryable<T> Query<T>()
        {
            return new CosmosQueryable<T>(_queryProvider);
        }

        public async Task<CosmosHttpResponse> Store(string id, object entity)
        {
            var resourceValue = _basePath.Substring(0, _basePath.LastIndexOf('/'));
            var json = _serializer.Serialize(entity);
            var result = await GetResourceResult("post", _basePath, resourceValue, json, upsert: true);
            return result;
        }

        private async Task<CosmosHttpResponse> GetResourceResult(string verb, string queryPath, string resourceValue = "", string body = "", string resourceType = "docs", bool jsonQuery = false, bool upsert = false)
        {
            try
            {
                var responseMessage = await HttpRequestHelper.ExecuteResourceRequest(verb, _uri, _apiKey, queryPath, "docs", resourceValue, body, jsonQuery, upsert);
                var response = new CosmosHttpResponse
                {
                    StatusCode = responseMessage.StatusCode,
                    Body = await responseMessage.Content.ReadAsStringAsync()
                };
                return response;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                throw;
            }
        }

        private string CreateCosmosQueryJson(string sql, object param)
        {
            var query = new PostCosmosQueryModel { Query = sql, Parameters = new List<CosmosQueryParameter>() };

            if (param != null)
            {
                var dictionary = param.GetType().GetProperties()
                .ToDictionary(x => x.Name, x => x.GetValue(param)?.ToString() ?? "");

                query.Parameters = DictionaryHelper.ToCosmosQueryParameterList(dictionary);
            }

            return _serializer.Serialize(query); // might have to use different method, this one adds type parameter
        }
    }
}