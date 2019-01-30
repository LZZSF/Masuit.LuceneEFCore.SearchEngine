﻿using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Masuit.LuceneEFCore.SearchEngine.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Masuit.LuceneEFCore.SearchEngine
{
    public class LuceneIndexSearcher : ILuceneIndexSearcher
    {
        private static Directory directory;
        private static Analyzer analyzer;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="directory">索引目录</param>
        /// <param name="analyzer">索引分析器</param>
        public LuceneIndexSearcher(Directory directory, Analyzer analyzer)
        {
            LuceneIndexSearcher.directory = directory;
            LuceneIndexSearcher.analyzer = analyzer;
        }

        /// <summary>
        /// 分词模糊查询
        /// </summary>
        /// <param name="parser">条件</param>
        /// <param name="keywords">关键词</param>
        /// <returns></returns>
        private BooleanQuery GetFuzzyquery(MultiFieldQueryParser parser, string keywords)
        {
            var finalQuery = new BooleanQuery();

            string[] terms = keywords.Split(new[] //todo:分词
            {
                " "
            }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var term in terms)
            {
                finalQuery.Add(parser.Parse(term.Replace("~", "") + "~"), Occur.MUST);
            }
            return finalQuery;
        }

        /// <summary>
        /// 执行搜索
        /// </summary>
        /// <param name="options">搜索选项</param>
        /// <param name="safeSearch">启用安全搜索</param>
        /// <returns></returns>
        private ILuceneSearchResultCollection PerformSearch(SearchOptions options, bool safeSearch)
        {
            // 结果集
            ILuceneSearchResultCollection results = new LuceneSearchResultCollection();

            using (var reader = DirectoryReader.Open(directory))
            {
                var searcher = new IndexSearcher(reader);
                Query query;

                // 启用安全搜索
                if (safeSearch)
                {
                    options.Keywords = QueryParserBase.Escape(options.Keywords);
                }

                if (options.Fields.Count == 1)
                {
                    // 单字段搜索
                    QueryParser queryParser = new QueryParser(Lucene.Net.Util.LuceneVersion.LUCENE_48, options.Fields[0], analyzer);
                    query = queryParser.Parse(options.Keywords);
                }
                else
                {
                    // 多字段搜索
                    MultiFieldQueryParser multiFieldQueryParser = new MultiFieldQueryParser(Lucene.Net.Util.LuceneVersion.LUCENE_48, options.Fields.ToArray(), analyzer, options.Boosts);
                    query = GetFuzzyquery(multiFieldQueryParser, options.Keywords);
                }

                List<SortField> sortFields = new List<SortField>
                {
                    SortField.FIELD_SCORE
                };

                // 排序规则处理
                foreach (var sortField in options.OrderBy)
                {
                    sortFields.Add(new SortField(sortField, SortFieldType.STRING));
                }

                Sort sort = new Sort(sortFields.ToArray());
                ScoreDoc[] matches = searcher.Search(query, null, options.MaximumNumberOfHits, sort, true, true).ScoreDocs;
                results.TotalHits = matches.Length;

                // 分页处理
                if (options.Skip.HasValue)
                {
                    matches = matches.Skip(options.Skip.Value).ToArray();
                }
                if (options.Take.HasValue)
                {
                    matches = matches.Take(options.Take.Value).ToArray();
                }

                // 创建结果集
                foreach (var match in matches)
                {
                    var id = match.Doc;
                    var doc = searcher.Doc(id);

                    // 过滤掉已经设置了类型的对象
                    if (options.Type != null)
                    {
                        var t = doc.Get("Type");
                        if (options.Type.AssemblyQualifiedName == t)
                        {
                            results.Results.Add(new LuceneSearchResult()
                            {
                                Score = match.Score,
                                Document = doc
                            });
                        }
                    }
                    else
                    {
                        results.Results.Add(new LuceneSearchResult()
                        {
                            Score = match.Score,
                            Document = doc
                        });
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 搜索单条记录
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        public Document ScoredSearchSingle(SearchOptions options)
        {
            options.MaximumNumberOfHits = 1;
            var results = ScoredSearch(options);
            return results.TotalHits > 0 ? results.Results.First().Document : null;
        }

        /// <summary>
        /// 按权重搜索
        /// </summary>
        /// <param name="options">搜索选项</param>
        /// <returns></returns>
        public ILuceneSearchResultCollection ScoredSearch(SearchOptions options)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            ILuceneSearchResultCollection results;

            try
            {
                results = PerformSearch(options, false);
            }
            catch (ParseException)
            {
                results = PerformSearch(options, true);
            }

            sw.Stop();
            results.Elapsed = sw.ElapsedMilliseconds;

            return results;
        }

        /// <summary>
        /// 按权重搜索
        /// </summary>
        /// <param name="keywords">关键词</param>
        /// <param name="fields">限定检索字段</param>
        /// <param name="maximumNumberOfHits">最大检索量</param>
        /// <param name="boosts">加速器</param>
        /// <param name="type">文档类型</param>
        /// <param name="sortBy">排序规则</param>
        /// <param name="skip">跳过多少条</param>
        /// <param name="take">取多少条</param>
        /// <returns></returns>
        public ILuceneSearchResultCollection ScoredSearch(string keywords, string fields, int maximumNumberOfHits, Dictionary<string, float> boosts, Type type, string sortBy, int? skip, int? take)
        {
            SearchOptions options = new SearchOptions(keywords, fields, maximumNumberOfHits, boosts, type, sortBy, skip, take);

            return ScoredSearch(options);
        }
    }
}