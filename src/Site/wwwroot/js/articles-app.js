const { createApp } = Vue;

createApp({
    data() {
        return {
            articles: [],
            facets: [],
            total: 0,
            loading: false,
            error: null,
            currentPage: 1,
            pageSize: 6,
            selectedAuthors: [],
            selectedCategories: [],
            searchQuery: '',
            sortBy: 'relevance',
            sortDirection: 'desc',
            minYear: null,
            maxYear: null,
            selectedMinYear: null,
            selectedMaxYear: null,
            yearChangeTimeout: null
        };
    },
    computed: {
        totalPages() {
            return Math.ceil(this.total / this.pageSize);
        },
        authorFacet() {
            return this.facets.find(f => f.fieldName === 'authorName');
        },
        categoryFacet() {
            return this.facets.find(f => f.fieldName === 'categoryName');
        },
        // Prefer daterange facet 'articleDate', fall back to integer facet 'articleYear'
        yearFacet() {
            return this.facets.find(f => f.fieldName === 'articleDate')
                || this.facets.find(f => f.fieldName === 'articleYear');
        },
        availableYears() {
            if (!this.yearFacet || !this.yearFacet.values) {
                return [];
            }
            // Handle multiple facet value shapes:
            // - DateRangeFacetValue: { key: "2023", min: "iso", max: "iso", count: number }
            // - KeywordFacetValue: { key: "2023", count: number }
            // - IntegerExactFacetValue: { value: 2023, count: number }
            return this.yearFacet.values
                .map(v => {
                    if (v.value !== undefined) { // IntegerExactFacetValue
                        return parseInt(v.value);
                    }
                    if (v.key !== undefined) { // Keyword or DateRange facet provides year in key
                        return parseInt(v.key);
                    }
                    return NaN;
                })
                .filter(year => !isNaN(year))
                .sort((a, b) => a - b);
        },
        yearRange() {
            if (this.availableYears.length === 0) {
                return { min: 2020, max: new Date().getFullYear() };
            }
            return {
                min: Math.min(...this.availableYears),
                max: Math.max(...this.availableYears)
            };
        },
        selectedYears() {
            // Only return years if the range is actually filtered (not the full range)
            if (this.selectedMinYear && this.selectedMaxYear) {
                const range = this.yearRange;
                if (range && range.min !== undefined && range.max !== undefined) {
                    const isFiltered = this.selectedMinYear > range.min || this.selectedMaxYear < range.max;
                    if (isFiltered) {
                        const years = [];
                        for (let year = this.selectedMinYear; year <= this.selectedMaxYear; year++) {
                            years.push(year.toString());
                        }
                        return years;
                    }
                }
            }
            return [];
        }
    },
    watch: {
        availableYears: {
            immediate: true,
            handler(newYears) {
                if (newYears.length > 0 && (!this.selectedMinYear || !this.selectedMaxYear)) {
                    this.selectedMinYear = this.yearRange.min;
                    this.selectedMaxYear = this.yearRange.max;
                }
            }
        }
    },
    mounted() {
        this.fetchArticles();
    },
    methods: {
        // Helper: find min/max ISO bounds for a given year from the daterange facet
        getYearBounds(year) {
            if (!this.yearFacet || this.yearFacet.fieldName !== 'articleDate') {
                return null;
            }
            const entry = (this.yearFacet.values || []).find(v => `${v.key}` === `${year}`);
            if (entry && entry.min && entry.max) {
                return { min: entry.min, max: entry.max };
            }
            return null;
        },
        // Helper: determine if the current year selection is filtered
        isYearRangeFiltered() {
            if (!this.selectedMinYear || !this.selectedMaxYear) return false;
            const range = this.yearRange;
            return this.selectedMinYear > range.min || this.selectedMaxYear < range.max;
        },
        // Build a daterange param "minISO,maxISO" from selected years and facet bounds
        buildArticleDateRangeParam() {
            if (!this.yearFacet || this.yearFacet.fieldName !== 'articleDate') return null;
            if (!this.isYearRangeFiltered()) return null;

            const minSel = this.selectedMinYear;
            const maxSel = this.selectedMaxYear;

            // Try to use facet-provided ISO bounds per year
            const minBounds = this.getYearBounds(minSel);
            const maxBounds = this.getYearBounds(maxSel);

            // Fallbacks in case facet entries are missing for edge years
            const fallbackMin = `${minSel}-01-01T00:00:00Z`;
            // End-of-year; depending on server logic min/max precision may not matter
            const fallbackMax = `${maxSel}-12-31T23:59:59Z`;

            const minIso = (minBounds && minBounds.min) ? minBounds.min : fallbackMin;
            const maxIso = (maxBounds && maxBounds.max) ? maxBounds.max : fallbackMax;

            return `${minIso},${maxIso}`;
        },
        async fetchArticles() {
            this.loading = true;
            this.error = null;
            
            try {
                const skip = (this.currentPage - 1) * this.pageSize;
                const params = new URLSearchParams({
                    skip: skip.toString(),
                    take: this.pageSize.toString()
                });
                
                if (this.searchQuery) {
                    params.append('query', this.searchQuery);
                }
                
                if (this.selectedAuthors.length > 0) {
                    this.selectedAuthors.forEach(author => {
                        params.append('author', author);
                    });
                }
                
                if (this.selectedCategories.length > 0) {
                    this.selectedCategories.forEach(category => {
                        params.append('categories', category);
                    });
                }
                
                // Year/date filtering
                // If we have a daterange facet ('articleDate'), send a single param "minISO,maxISO"
                const dateRangeParam = this.buildArticleDateRangeParam();
                if (dateRangeParam) {
                    params.append('articleDate', dateRangeParam);
                } else {
                    // Backward compatibility: if we only have an integer/keyword year facet ('articleYear'),
                    // append each year in the filtered range as separate 'articleYear' params
                    if (this.yearFacet && this.yearFacet.fieldName === 'articleYear') {
                        const yearYears = this.selectedYears;
                        if (yearYears && yearYears.length > 0) {
                            yearYears.forEach(year => {
                                params.append('articleYear', year);
                            });
                        }
                    }
                }
                
                if (this.sortBy) {
                    params.append('sortBy', this.sortBy);
                }
                
                if (this.sortDirection) {
                    params.append('sortDirection', this.sortDirection);
                }
                
                const response = await fetch(`/api/articles?${params.toString()}`);
                
                if (!response.ok) {
                    throw new Error(`HTTP error! status: ${response.status}`);
                }
                
                const data = await response.json();
                this.articles = data.documents || [];
                this.facets = data.facets || [];
                this.total = data.total || 0;
                
                // Update year range if not set or if facets changed
                if (this.availableYears.length > 0) {
                    const newRange = this.yearRange;
                    // Initialize if not set
                    if (!this.selectedMinYear || !this.selectedMaxYear) {
                        this.selectedMinYear = newRange.min;
                        this.selectedMaxYear = newRange.max;
                    } else {
                        // Adjust if outside available range
                        if (this.selectedMinYear < newRange.min) {
                            this.selectedMinYear = newRange.min;
                        }
                        if (this.selectedMaxYear > newRange.max) {
                            this.selectedMaxYear = newRange.max;
                        }
                    }
                }
            } catch (error) {
                this.error = error.message;
                console.error('Error fetching articles:', error);
            } finally {
                this.loading = false;
            }
        },
        toggleAuthor(author) {
            const index = this.selectedAuthors.indexOf(author);
            if (index > -1) {
                this.selectedAuthors.splice(index, 1);
            } else {
                this.selectedAuthors.push(author);
            }
            this.currentPage = 1;
            this.fetchArticles();
        },
        toggleCategory(category) {
            const index = this.selectedCategories.indexOf(category);
            if (index > -1) {
                this.selectedCategories.splice(index, 1);
            } else {
                this.selectedCategories.push(category);
            }
            this.currentPage = 1;
            this.fetchArticles();
        },
        goToPage(page) {
            if (page >= 1 && page <= this.totalPages) {
                this.currentPage = page;
                this.fetchArticles();
                window.scrollTo({ top: 0, behavior: 'smooth' });
            }
        },
        search() {
            this.currentPage = 1;
            this.fetchArticles();
        },
        onSortChange() {
            this.currentPage = 1;
            this.fetchArticles();
        },
        onYearRangeChange() {
            // Ensure min <= max
            if (this.selectedMinYear > this.selectedMaxYear) {
                const temp = this.selectedMinYear;
                this.selectedMinYear = this.selectedMaxYear;
                this.selectedMaxYear = temp;
            }
            // Immediately update for better responsiveness
            this.currentPage = 1;
            this.fetchArticles();
        },
        formatDate(dateString) {
            if (!dateString) return '';
            const date = new Date(dateString);
            return date.toLocaleDateString('en-US', { 
                year: 'numeric', 
                month: 'long', 
                day: 'numeric' 
            });
        },
        getArticleImage(article) {
            if (article?.properties?.mainImage && article.properties.mainImage.length > 0) {
                return article.properties.mainImage[0].url;
            }
            return null;
        },
        getAuthorName(article) {
            if (article?.properties?.author && article.properties.author.length > 0) {
                return article.properties.author[0].name;
            }
            return null;
        },
        getCategories(article) {
            if (article?.properties?.categories && article.properties.categories.length > 0) {
                return article.properties.categories.map(cat => cat.name).join(', ');
            }
            return null;
        }
    }
}).mount('#articles-app');

