import EasyMDE from "easymde";

export function codeeditor() {
    return {
        initialize: initialize,
        saveContentToDisk: saveContentToDisk,
        setupCodeEditor: setupCodeEditor,
        updateContent: updateContent,
        populateCategories: populateCategories,
        getinstance: getinstance
    }
}

function setupCodeEditor(elementId) {
    console.log('Page loaded without refresh');

    const easymde = initialize(elementId);
    // Trigger on change event of EasyMDE editor
    easymde.codemirror.on("keydown", function(instance, event) {
        let triggerUpdate = false;
        if ((event.ctrlKey || event.metaKey) && event.altKey && event.key.toLowerCase() === "r") {
            event.preventDefault();
            triggerUpdate = true;
        }
        if (event.key === "Enter") {
            triggerUpdate = true;
        }
        if (triggerUpdate) {
            updateContent(easymde);
        }
    });
}

function populateCategories(categories) {
    var categoriesDiv = document.getElementById('categories');
    categoriesDiv.innerHTML = '';

    categories.forEach(function(category) {
        let span = document.createElement('span');
        span.className = 'inline-block rounded-full dark bg-blue-dark px-2 py-1 font-body text-sm text-white outline-1 outline outline-green-dark dark:outline-white mr-2';
        span.textContent = category;
        categoriesDiv.appendChild(span);
    });
}

function updateContent(easymde) {
    var content = easymde.value();

    fetch('/api/editor/getcontent', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({ content: content })
    })
        .then(response => response.json())
        .then(data => {
            document.getElementById('renderedcontent').innerHTML = data.htmlContent;
            document.getElementById('title').innerHTML = data.title;
            const date = new Date(data.publishedDate);

            const formattedDate = new Intl.DateTimeFormat('en-GB', {
                weekday: 'long',
                day: 'numeric',
                month: 'long',
                year: 'numeric'
            }).format(date);

            document.getElementById('publishedDate').innerHTML = formattedDate;
            window.mostlylucid.simplemde.populateCategories(data.categories);

            mermaid.run();
            hljs.highlightAll();
        })
        .catch(error => console.error('Error:', error));
}

function initialize(elementId, reducedToolbar = false) {
    if (!window.simplemdeInstances) {
        window.simplemdeInstances = {};
    }

    const element = document.getElementById(elementId);
    if (!element) return;

    if (window.simplemdeInstances[elementId]) {
        window.simplemdeInstances[elementId].toTextArea();
        window.simplemdeInstances[elementId] = null;
    }

    let easymdeInstance = {};

    if (reducedToolbar) {
        easymdeInstance = new EasyMDE({
            element: element,
            toolbar: [
                "bold", "italic", "heading", "|", "quote", "unordered-list", "ordered-list", "|"
            ]
        });
    } else {
        easymdeInstance = new EasyMDE({
            forceSync: true,
            renderingConfig: {
                singleLineBreaks: true,
                codeSyntaxHighlighting: true,
            },
            element: element,
            toolbar: [
                "bold", "italic", "heading", "|", "quote", "unordered-list", "ordered-list", "|",
                {
                    name: "save",
                    action: function (editor) {
                        var params = new URLSearchParams(window.location.search);
                        var slug = params.get("slug");
                        var language = params.get("language") || "en";

                        saveContentToDisk(editor.value(), slug, language);
                    },
                    className: "bx bx-save",
                    title: "Save"
                },
                "|",
                {
                    name: "insert-category",
                    action: function (editor) {
                        var category = prompt("Enter categories separated by commas", "EasyNMT, ASP.NET, C#");
                        if (category) {
                            var currentContent = editor.value();
                            var categoryTag = `<!--category-- ${category} -->\n\n`;
                            editor.value(currentContent + categoryTag);
                        }
                    },
                    className: "bx bx-tag",
                    title: "Insert Categories"
                },
                "|",
                {
                    name: "update",
                    action: function () {
                        updateContent(getinstance(elementId));
                    },
                    className: "bx bx-refresh",
                    title: "Update"
                },
                "|",
                {
                    name: "insert-datetime",
                    action: function (editor) {
                        var now = new Date();
                        var formattedDateTime = now.toISOString().slice(0, 16);
                        var datetimeTag = `<datetime class="hidden">${formattedDateTime}</datetime>\n\n`;

                        var currentContent = editor.value();
                        editor.value(currentContent + datetimeTag);
                    },
                    className: "bx bx-calendar",
                    title: "Insert Datetime"
                },
                "|", "preview", "side-by-side", "fullscreen"
            ]
        });
    }

    window.simplemdeInstances[elementId] = easymdeInstance;
    return easymdeInstance;
}

function saveContentToDisk(content, slug, language) {
    console.log("Saving content to disk...");

    var filename = (slug || "untitled") + (language === 'en' ? ".md" : `.${language}.md`);
    var blob = new Blob([content], { type: "text/markdown;charset=utf-8;" });
    var link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = filename;

    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);

    console.log("Download triggered for " + filename);
}

function getinstance(elementId) {
    return window.simplemdeInstances ? window.simplemdeInstances[elementId] : null;
}