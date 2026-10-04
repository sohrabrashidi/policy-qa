# policy-qa

Ask questions about your internal policy documents and get answers with citations to the exact section, built in C# / ASP.NET Core (.NET 10).

The use case comes from regulated businesses like exchange houses, payment companies and small banks. Staff constantly need answers like *"how long do we keep STR files?"* or *"does a PEP need compliance sign-off?"*, and the answer is buried in a 40-page PDF. A generic chatbot that guesses is worse than useless here. So this project is built around one rule: **no citation, no answer.**

The repo ships with three short sample policies for a fictional exchange company, so you can run it and ask questions straight away.

## How it works

```
 Markdown / text  ──►  section-aware chunker  ──►  BM25 keyword index
                                             └──►  vector index (embeddings)

 question  ──►  hybrid search (BM25 + vectors, merged with Reciprocal Rank Fusion)
           ──►  top-k extracts, numbered [1]..[k]
           ──►  LLM with a strict "answer only from extracts, cite every sentence" prompt
           ──►  citation check: invented source numbers are removed,
                answers with no valid citation are flagged as not grounded
```

A few decisions worth explaining:

- **Chunks never cross a heading.** Policies are written in numbered sections. Mixing the end of section 4.1 with the start of 4.2 is the most common reason a RAG system gives a confident wrong answer. Each chunk also carries its heading path (`4. Enhanced due diligence > 4.2 What EDD involves`), which is what the user sees as the citation.
- **Hybrid search, not just embeddings.** Compliance questions often depend on exact terms like "PEP", "STR", "3,000 KWD" or "5 years". Embeddings are good with paraphrases, keyword search is good with these terms, and RRF combines the two rankings without any score tuning.
- **The answer is checked after generation.** The model is asked to cite `[n]` after each sentence. The service strips references to sources that don't exist and marks the answer `grounded: false` if nothing valid is left. The UI can then show a warning instead of a confident paragraph.
- **Works without an API key.** With no key configured, it uses local hashed embeddings and returns the best matching sentences with their sources. It's less fluent, but it never makes anything up, and it's how the tests run.
- **Any OpenAI-compatible endpoint.** The client talks to `/embeddings` and `/chat/completions` over plain `HttpClient`, so OpenAI, Azure OpenAI, Ollama, LM Studio or vLLM all work by changing `Llm:BaseUrl`.

## Running it

```bash
dotnet run --project src/PolicyQa.Api
```

Then:

```bash
curl -s localhost:5090/ask -H "content-type: application/json" \
  -d '{"question":"How quickly must a held transfer be reviewed?"}'
```

```json
{
  "question": "How quickly must a held transfer be reviewed?",
  "text": "Held transfers must be reviewed within 4 working hours. [1]",
  "citations": [
    {
      "number": 1,
      "document": "Transaction Monitoring Procedure",
      "section": "2. Real-time screening > 2.2 Automatic holds",
      "excerpt": "A transfer is held automatically when: ..."
    }
  ],
  "grounded": true,
  "mode": "extractive"
}
```

To use a real model, set the key (user secrets or an environment variable):

```bash
export Llm__ApiKey=sk-...
# optional: point at a local model instead
export Llm__BaseUrl=http://localhost:11434/v1/
export Llm__ChatModel=llama3.1
export Llm__EmbeddingModel=nomic-embed-text
```

Upload your own documents:

```bash
curl -F "file=@my-policy.md" localhost:5090/documents
```

Or point `DocumentsFolder` at a folder of `.md` / `.txt` files to index on startup.

**Docker:**

```bash
docker build -t policy-qa .
docker run -p 8080:8080 -e Llm__ApiKey=sk-... policy-qa
```

**Tests:**

```bash
dotnet test PolicyQa.slnx
```

The tests cover chunking, retrieval quality on the sample documents (right section ranked first), citation cleanup and the "not covered" path, all offline.

## Project layout

```
src/PolicyQa.Core
  Ingestion/   document loading, section-aware chunking
  Retrieval/   tokenizer, BM25, vector index, hybrid retriever (RRF)
  Llm/         OpenAI-compatible client, offline hashing embedder
  Answering/   prompt, citation parsing, answer service
src/PolicyQa.Api      minimal API: /ask, /documents
tests/PolicyQa.Tests
sample-docs/          fictional policies used for the demo and tests
```

## Limitations and next steps

- PDF and Word input. Today it reads Markdown and text. For real policy libraries I'd add a PdfPig / OpenXML loader that keeps the heading structure.
- Indexes live in memory and are rebuilt on startup. For more than a few thousand chunks I'd move vectors to PostgreSQL + pgvector.
- No authentication. In a real deployment this sits behind the company's SSO, and documents are filtered by the user's department.
- An evaluation set (questions + expected sections) run in CI, to catch retrieval regressions when chunking or models change.

## License

MIT
