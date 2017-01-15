﻿using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeImp.DoomBuilder.ZDoom
{
    public sealed class ZScriptParser : ZDTextParser
    {
        #region ================== Delegates

        public delegate void IncludeDelegate(ZScriptParser parser, string includefile);

        public IncludeDelegate OnInclude;

        #endregion

        #region ================== Constants

        #endregion

        #region ================== Variables

        //mxd. Script type
        internal override ScriptType ScriptType { get { return ScriptType.ZSCRIPT; } }

        // These are actors we want to keep
        private Dictionary<string, ActorStructure> actors;

        // These are all parsed actors, also those from other games
        private Dictionary<string, ActorStructure> archivedactors;

        //mxd. Includes tracking
        private readonly HashSet<string> parsedlumps;

        //mxd. Custom damagetypes
        private readonly HashSet<string> damagetypes;

        //mxd. Disposing. Is that really needed?..
        private bool isdisposed;

        // [ZZ] custom tokenizer class
        private ZScriptTokenizer tokenizer;

        #endregion

        #region ================== Properties

        /// <summary>
        /// All actors that are supported by the current game.
        /// </summary>
        public IEnumerable<ActorStructure> Actors { get { return actors.Values; } }

        /// <summary>
        /// All actors defined in the loaded DECORATE structures. This includes actors not supported in the current game.
        /// </summary>
        public ICollection<ActorStructure> AllActors { get { return archivedactors.Values; } }

        /// <summary>
        /// mxd. All actors that are supported by the current game.
        /// </summary>
        internal Dictionary<string, ActorStructure> ActorsByClass { get { return actors; } }

        /// <summary>
        /// mxd. All actors defined in the loaded DECORATE structures. This includes actors not supported in the current game.
        /// </summary>
        internal Dictionary<string, ActorStructure> AllActorsByClass { get { return archivedactors; } }

        #endregion

        #region ================== Constructor / Disposer

        // Constructor
        public ZScriptParser()
        {
            // Initialize
            actors = new Dictionary<string, ActorStructure>(StringComparer.OrdinalIgnoreCase);
            archivedactors = new Dictionary<string, ActorStructure>(StringComparer.OrdinalIgnoreCase);
            parsedlumps = new HashSet<string>(StringComparer.OrdinalIgnoreCase); //mxd
            damagetypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase); //mxd
        }

        // Disposer
        public void Dispose()
        {
            //mxd. Not already disposed?
            if (!isdisposed)
            {
                foreach (KeyValuePair<string, ActorStructure> a in archivedactors)
                    a.Value.Dispose();

                actors = null;
                archivedactors = null;

                isdisposed = true;
            }
        }

        #endregion

        #region ================== Parsing

        private bool ParseInclude(string filename)
        {
            Stream localstream = datastream;
            string localsourcename = sourcename;
            BinaryReader localreader = datareader;
            DataLocation locallocation = datalocation; //mxd
            string localtextresourcepath = textresourcepath; //mxd
            ZScriptTokenizer localtokenizer = tokenizer; // [ZZ]

            //INFO: ZDoom DECORATE include paths can't be relative ("../actor.txt") 
            //or absolute ("d:/project/actor.txt") 
            //or have backward slashes ("info\actor.txt")
            //include paths are relative to the first parsed entry, not the current one 
            //also include paths may or may not be quoted
            //mxd. Sanity checks
            if (string.IsNullOrEmpty(filename))
            {
                ReportError("Expected file name to include");
                return false;
            }

            //mxd. Check invalid path chars
            if (!CheckInvalidPathChars(filename)) return false;

            //mxd. Absolute paths are not supported...
            if (Path.IsPathRooted(filename))
            {
                ReportError("Absolute include paths are not supported by ZDoom");
                return false;
            }

            //mxd. Relative paths are not supported
            if (filename.StartsWith(RELATIVE_PATH_MARKER) || filename.StartsWith(CURRENT_FOLDER_PATH_MARKER) ||
                filename.StartsWith(ALT_RELATIVE_PATH_MARKER) || filename.StartsWith(ALT_CURRENT_FOLDER_PATH_MARKER))
            {
                ReportError("Relative include paths are not supported by ZDoom");
                return false;
            }

            //mxd. Backward slashes are not supported
            if (filename.Contains(Path.DirectorySeparatorChar.ToString(CultureInfo.InvariantCulture)))
            {
                ReportError("Only forward slashes are supported by ZDoom");
                return false;
            }

            //mxd. Already parsed?
            if (parsedlumps.Contains(filename))
            {
                ReportError("Already parsed \"" + filename + "\". Check your include directives");
                return false;
            }

            //mxd. Add to collection
            parsedlumps.Add(filename);

            // Callback to parse this file now
            if (OnInclude != null) OnInclude(this, filename);

            //mxd. Bail out on error
            if (this.HasError) return false;

            // Set our buffers back to continue parsing
            datastream = localstream;
            datareader = localreader;
            sourcename = localsourcename;
            datalocation = locallocation; //mxd
            textresourcepath = localtextresourcepath; //mxd
            tokenizer = localtokenizer;

            return true;
        }

        // read in an expression as a token list.
        private List<ZScriptToken> ParseExpression()
        {
            List<ZScriptToken> ol = new List<ZScriptToken>();
            //
            int nestingLevel = 0;
            //
            while (true)
            {
                long cpos = datastream.Position;
                ZScriptToken token = tokenizer.ReadToken();
                if (token == null)
                {
                    ReportError("Expected a token.");
                    return null;
                }

                if ((token.Type == ZScriptTokenType.Semicolon ||
                     token.Type == ZScriptTokenType.Comma) && nestingLevel == 0)
                {
                    datastream.Position = cpos;
                    return ol;
                }
                
                if (token.Type == ZScriptTokenType.OpenParen)
                {
                    nestingLevel++;
                }
                else if (token.Type == ZScriptTokenType.CloseParen)
                {
                    nestingLevel--;
                    if (nestingLevel < 0) // for example, function call
                    {
                        datastream.Position = cpos;
                        return ol;
                    }
                }

                ol.Add(token);
            }
        }

        private List<ZScriptToken> ParseBlock(bool allowsingle)
        {
            List<ZScriptToken> ol = new List<ZScriptToken>();
            //
            int nestingLevel = 0;
            //
            long cpos = datastream.Position;
            ZScriptToken token = tokenizer.ReadToken();
            if (token == null)
            {
                ReportError("Expected a code block, got <null>");
                return null;
            }

            if (token.Type != ZScriptTokenType.OpenCurly)
            {
                if (!allowsingle)
                {
                    ReportError("Expected opening curly brace, got " + token);
                    return null;
                }

                // otherwise this is an expression
                datastream.Position = cpos;
                List<ZScriptToken> ol_expression = ParseExpression();
                token = tokenizer.ReadToken();
                if (token == null || token.Type != ZScriptTokenType.Semicolon)
                {
                    ReportError("Expected ;, got " + ((Object)token ?? "<null>").ToString());
                    return null;
                }

                ol_expression.Add(token);
                return ol_expression;
            }

            // parse everything between { and }
            nestingLevel = 1;
            while (nestingLevel > 0)
            {
                cpos = datastream.Position;
                token = tokenizer.ReadToken();
                if (token == null)
                {
                    ReportError("Expected a token.");
                    return null;
                }

                if (token.Type == ZScriptTokenType.OpenCurly)
                {
                    nestingLevel++;
                }
                else if (token.Type == ZScriptTokenType.CloseCurly)
                {
                    nestingLevel--;
                    if (nestingLevel < 0)
                    {
                        ReportError("Closing parenthesis without an opening one!");
                        return null;
                    }
                }

                ol.Add(token);
            }

            // there is POTENTIALLY a semicolon after the class definition. it's not supposed to be there, but it's acceptable (GZDoom.pk3 has this)
            ZScriptToken tailtoken = tokenizer.ReadToken();
            cpos = datastream.Position;
            if (tailtoken == null || tailtoken.Type != ZScriptTokenType.Semicolon)
                datastream.Position = cpos;
            else ol.Add(tailtoken);

            return ol;
        }

        private bool ParseClassOrStruct(bool isstruct)
        {
            // 'class' keyword is already parsed
            tokenizer.SkipWhitespace();
            ZScriptToken tok_classname = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
            if (tok_classname == null || !tok_classname.IsValid)
            {
                ReportError("Expected class name, got " + ((Object)tok_classname ?? "<null>").ToString());
                return false;
            }

            // name [replaces name] [: name] [native]
            ZScriptToken tok_replacename = null;
            ZScriptToken tok_parentname = null;
            ZScriptToken tok_native = null;
            int tokens = 0;
            while (tokens++ < 4)
            {
                tokenizer.SkipWhitespace();
                ZScriptToken token = tokenizer.ReadToken();

                if (token == null)
                {
                    ReportError("Expected a token.");
                    return false;
                }

                if (token.Type == ZScriptTokenType.Identifier)
                {
                    if (token.Value.ToLowerInvariant() == "replaces")
                    {
                        if (tok_native != null)
                        {
                            ReportError("Cannot have replacement after native.");
                            return false;
                        }

                        if (tok_replacename != null)
                        {
                            ReportError("Cannot have two replacements per class.");
                            return false;
                        }

                        tokenizer.SkipWhitespace();
                        tok_replacename = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                        if (tok_replacename == null || !tok_replacename.IsValid)
                        {
                            ReportError("Expected replacement class name, got " + ((Object)tok_replacename ?? "<null>").ToString());
                            return false;
                        }
                    }
                    else if (token.Value.ToLowerInvariant() == "native")
                    {
                        if (tok_native != null)
                        {
                            ReportError("Cannot have two native keywords.");
                            return false;
                        }

                        tok_native = token;
                    }
                    else
                    {
                        ReportError("Unexpected token " + ((Object)token ?? "<null>").ToString());
                    }
                }
                else if (token.Type == ZScriptTokenType.Colon)
                {
                    if (tok_parentname != null)
                    {
                        ReportError("Cannot have two parent classes.");
                        return false;
                    }

                    if (tok_replacename != null || tok_native != null)
                    {
                        ReportError("Cannot have parent class after replacement class or native keyword.");
                        return false;
                    }

                    tokenizer.SkipWhitespace();
                    tok_parentname = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                    if (tok_parentname == null || !tok_parentname.IsValid)
                    {
                        ReportError("Expected replacement class name, got " + ((Object)tok_parentname ?? "<null>").ToString());
                        return false;
                    }
                }
                else if (token.Type == ZScriptTokenType.OpenCurly)
                {
                    datastream.Position--;
                    break;
                }
            }

            // do nothing else atm
            List<ZScriptToken> classblocktokens = ParseBlock(false);
            if (classblocktokens == null) return false;

            string log_inherits = ((tok_parentname != null) ? "inherits " + tok_parentname.Value : "");
            if (tok_replacename != null) log_inherits += ((log_inherits.Length > 0) ? ", " : "") + "replaces " + tok_replacename.Value;
            LogWarning(string.Format("parsed {0} {1} {2}", isstruct ? "struct" : "class", tok_classname.Value, log_inherits));

            return true;
        }

        // This parses the given decorate stream
        // Returns false on errors
        public override bool Parse(TextResourceData data, bool clearerrors)
        {
            //mxd. Already parsed?
            if (!base.AddTextResource(data))
            {
                if (clearerrors) ClearError();
                return true;
            }

            // Cannot process?
            if (!base.Parse(data, clearerrors)) return false;

            // [ZZ] For whatever reason, the parser is closely tied to the tokenizer, and to the general scripting lumps framework (see scripttype).
            //      For this reason I have to still inherit the old tokenizer while only using the new one.
            //ReportError("found zscript? :)");
            prevstreamposition = -1;
            tokenizer = new ZScriptTokenizer(datareader);

            while (true)
            {
                ZScriptToken token = tokenizer.ExpectToken(ZScriptTokenType.Identifier, // const, enum, class, etc
                                                           ZScriptTokenType.Whitespace,
                                                           ZScriptTokenType.Newline,
                                                           ZScriptTokenType.BlockComment, ZScriptTokenType.LineComment,
                                                           ZScriptTokenType.Preprocessor);

                if (token == null) // EOF reached, whatever.
                    break;

                if (!token.IsValid)
                {
                    ReportError("Expected preprocessor statement, const, enum or class declaraction, got " + token);
                    return false;
                }

                // toplevel tokens allowed are only Preprocessor and Identifier.
                switch (token.Type)
                {
                    case ZScriptTokenType.Whitespace:
                    case ZScriptTokenType.Newline:
                    case ZScriptTokenType.BlockComment:
                    case ZScriptTokenType.LineComment:
                        break;

                    case ZScriptTokenType.Preprocessor:
                        {
                            tokenizer.SkipWhitespace();
                            ZScriptToken directive = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                            if (directive == null || !directive.IsValid)
                            {
                                ReportError("Expected preprocessor directive, got " + ((Object)directive ?? "<null>").ToString());
                                return false;
                            }

                            if (directive.Value.ToLowerInvariant() == "include")
                            {
                                tokenizer.SkipWhitespace();
                                ZScriptToken include_name = tokenizer.ExpectToken(ZScriptTokenType.Identifier, ZScriptTokenType.String, ZScriptTokenType.Name);
                                if (include_name == null || !include_name.IsValid)
                                {
                                    ReportError("Cannot include: expected a string value, got " + ((Object)include_name ?? "<null>").ToString());
                                    return false;
                                }

                                if (!ParseInclude(include_name.Value))
                                    return false;
                            }
                            else
                            {
                                ReportError("Unknown preprocessor directive: " + directive.Value);
                                return false;
                            }
                            break;
                        }

                    case ZScriptTokenType.Identifier:
                        {
                            // identifier can be one of: class, enum, const, struct
                            // the only type that we really care about is class, as it's the one that has all actors.
                            switch (token.Value.ToLowerInvariant())
                            {
                                case "class":
                                    // todo parse class
                                    if (!ParseClassOrStruct(false)) return false;
                                    break;
                                case "struct":
                                    // todo parse struct
                                    if (!ParseClassOrStruct(true)) return false;
                                    break;
                                case "const":
                                    // const blablabla = <expression>;
                                    tokenizer.SkipWhitespace();
                                    token = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                                    if (token == null || !token.IsValid)
                                    {
                                        ReportError("Expected const name, got " + ((Object)token ?? "<null>").ToString());
                                        return false;
                                    }
                                    string constname = token.Value;
                                    tokenizer.SkipWhitespace();
                                    token = tokenizer.ExpectToken(ZScriptTokenType.OpAssign);
                                    if (token == null || !token.IsValid)
                                    {
                                        ReportError("Expected =, got " + ((Object)token ?? "<null>").ToString());
                                        return false;
                                    }
                                    tokenizer.SkipWhitespace();
                                    if (ParseExpression() == null) return false; // anything until a semicolon or a comma, + anything between parentheses
                                    tokenizer.SkipWhitespace();
                                    token = tokenizer.ExpectToken(ZScriptTokenType.Semicolon);
                                    if (token == null || !token.IsValid)
                                    {
                                        ReportError("Expected ;, got " + ((Object)token ?? "<null>").ToString());
                                        return false;
                                    }
                                    LogWarning(string.Format("Parsed const {0}", constname));
                                    break;
                                case "enum":
                                    // enum blablabla {}
                                    tokenizer.SkipWhitespace();
                                    token = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                                    if (token == null || !token.IsValid)
                                    {
                                        ReportError("Expected enum name, got " + ((Object)token ?? "<null>").ToString());
                                        return false;
                                    }
                                    tokenizer.SkipWhitespace();
                                    if (ParseBlock(false) == null) return false; // anything between { and }
                                    LogWarning(string.Format("Parsed enum {0}", token.Value));
                                    break;
                            }
                            break;
                        }
                }
            }

            return true;
        }

        #endregion

        #region ================== Methods

        /// <summary>
        /// This returns a supported actor by name. Returns null when no supported actor with the specified name can be found. This operation is of O(1) complexity.
        /// </summary>
        public ActorStructure GetActorByName(string name)
        {
            name = name.ToLowerInvariant();
            return actors.ContainsKey(name) ? actors[name] : null;
        }

        /// <summary>
        /// This returns a supported actor by DoomEdNum. Returns null when no supported actor with the specified name can be found. Please note that this operation is of O(n) complexity!
        /// </summary>
        public ActorStructure GetActorByDoomEdNum(int doomednum)
        {
            foreach (ActorStructure a in actors.Values)
                if (a.DoomEdNum == doomednum) return a;
            return null;
        }

        // This returns an actor by name
        // Returns null when actor cannot be found
        internal ActorStructure GetArchivedActorByName(string name)
        {
            name = name.ToLowerInvariant();
            return (archivedactors.ContainsKey(name) ? archivedactors[name] : null);
        }

        #endregion

    }
}