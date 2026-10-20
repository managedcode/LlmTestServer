export default {
  extends: ['stylelint-config-standard'],
  rules: {
    // Component rules are scoped by their own class (.legend, .sankey, .capability…) and never
    // target the same element; source order follows the page, not global specificity.
    'no-descending-specificity': null,
  },
};
